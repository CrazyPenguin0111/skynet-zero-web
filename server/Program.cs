using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SkynetZero.Server;

if(args is ["--engine-worker"]){await NativeSearch.Worker();return;}

var builder=WebApplication.CreateBuilder(args);
string data=Path.GetFullPath(builder.Configuration["DataPath"]??Path.Combine(builder.Environment.ContentRootPath,"data"));Directory.CreateDirectory(data);
builder.Services.AddDbContext<PlatformDb>(o=>o.UseSqlite($"Data Source={Path.Combine(data,"platform.db")};Default Timeout=15"));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data,"keys"))).SetApplicationName("SkynetZero");
builder.Services.AddIdentity<Player,IdentityRole>(o=>{
    o.Password.RequiredLength=12;o.Password.RequireNonAlphanumeric=false;o.Password.RequireDigit=false;o.Password.RequireUppercase=false;o.Password.RequireLowercase=false;
    o.Lockout.MaxFailedAccessAttempts=6;o.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(15);
    o.User.AllowedUserNameCharacters="abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-";
}).AddEntityFrameworkStores<PlatformDb>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o=>{
    o.Cookie.Name="skynet.session";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Lax;
    o.Cookie.SecurePolicy=builder.Environment.IsDevelopment()||builder.Environment.IsEnvironment("Testing")?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;
    o.ExpireTimeSpan=TimeSpan.FromDays(14);o.SlidingExpiration=true;
    o.Events.OnRedirectToLogin=c=>{c.Response.StatusCode=401;return c.Response.WriteAsJsonAsync(new{error="Sign in to continue."});};
    o.Events.OnRedirectToAccessDenied=c=>{c.Response.StatusCode=403;return c.Response.WriteAsJsonAsync(new{error="You do not have access to this action."});};
});
builder.Services.AddAntiforgery(o=>{
    o.HeaderName="X-CSRF";o.Cookie.Name="skynet.csrf";o.Cookie.SameSite=SameSiteMode.Strict;
    o.Cookie.SecurePolicy=builder.Environment.IsDevelopment()||builder.Environment.IsEnvironment("Testing")?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;
});
bool discord=!string.IsNullOrWhiteSpace(builder.Configuration["Discord:ClientId"])&&!string.IsNullOrWhiteSpace(builder.Configuration["Discord:ClientSecret"]);
if(discord)builder.Services.AddAuthentication().AddOAuth("Discord",o=>{
    o.SignInScheme=IdentityConstants.ExternalScheme;o.ClientId=builder.Configuration["Discord:ClientId"]!;o.ClientSecret=builder.Configuration["Discord:ClientSecret"]!;
    o.CallbackPath="/signin-discord";o.AuthorizationEndpoint="https://discord.com/oauth2/authorize";o.TokenEndpoint="https://discord.com/api/oauth2/token";o.UserInformationEndpoint="https://discord.com/api/v10/users/@me";
    o.Scope.Add("identify");o.SaveTokens=false;
    o.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier,"id");o.ClaimActions.MapJsonKey(ClaimTypes.Name,"username");
    o.Events.OnCreatingTicket=async c=>{
        using var request=new HttpRequestMessage(HttpMethod.Get,c.Options.UserInformationEndpoint);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",c.AccessToken);
        using var response=await c.Backchannel.SendAsync(request,c.HttpContext.RequestAborted);response.EnsureSuccessStatusCode();
        using var user=JsonDocument.Parse(await response.Content.ReadAsStringAsync(c.HttpContext.RequestAborted));c.RunClaimActions(user.RootElement);
    };
    o.Events.OnRemoteFailure=c=>{c.HandleResponse();c.Response.Redirect("/#account?discord=failed");return Task.CompletedTask;};
});
builder.Services.AddAuthorization();
builder.Services.AddHttpsRedirection(o=>o.HttpsPort=443);
builder.Services.ConfigureHttpJsonOptions(o=>{
    o.SerializerOptions.RespectNullableAnnotations=true;
    o.SerializerOptions.RespectRequiredConstructorParameters=true;
});
builder.Services.AddRateLimiter(o=>{
    o.RejectionStatusCode=429;
    o.GlobalLimiter=PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext,string>(ctx=>
        !ctx.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetNoLimiter("static")
            : RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)??ctx.Connection.RemoteIpAddress?.ToString()??"anonymous",_=>new(){PermitLimit=300,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    o.AddPolicy("auth",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"anonymous",_=>new(){PermitLimit=12,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    o.AddPolicy("search",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)??"anonymous",_=>new(){PermitLimit=6,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
builder.Services.AddSingleton<NativeSearch>();
builder.Services.AddSingleton<WriteGate>();builder.Services.AddSingleton<Rules>();builder.Services.AddSingleton<EventFeed>();builder.Services.AddSingleton<TimeProvider,MonotonicClock>();
builder.Services.AddScoped<GameService>();builder.Services.AddScoped<TournamentService>();builder.Services.AddHostedService<PlatformTicker>();
builder.WebHost.ConfigureKestrel(o=>{o.Limits.MaxRequestBodySize=2_000_000;o.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(15);});
var app=builder.Build();
using(var scope=app.Services.CreateScope()){
    var db=scope.ServiceProvider.GetRequiredService<PlatformDb>();await db.Database.MigrateAsync();await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    if(args is ["--import-league",var file]){var imported=await LeagueImport.Run(db,file);Console.WriteLine($"Imported {imported.Players} players and {imported.Records} history rows.");return;}
    if(args is ["--grant-admin",var name]){
        var user=await scope.ServiceProvider.GetRequiredService<UserManager<Player>>().FindByNameAsync(name)??throw new InvalidOperationException("Register this account first.");
        user.IsAdmin=true;await db.SaveChangesAsync();Console.WriteLine("Administrator granted.");return;
    }
}
// Trust only explicitly configured proxy IPs; never accept arbitrary forwarded host headers.
string? proxy=builder.Configuration["TrustedProxy"];
if(!string.IsNullOrWhiteSpace(proxy)){
    var forwarding=new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions{ForwardedHeaders=Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor|Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto};
    forwarding.KnownProxies.Clear();forwarding.KnownIPNetworks.Clear();
    forwarding.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));app.UseForwardedHeaders(forwarding);
}
if(!app.Environment.IsDevelopment()&&!app.Environment.IsEnvironment("Testing")){app.UseHsts();app.UseHttpsRedirection();}
app.Use(async(ctx,next)=>{
    ctx.Response.Headers["X-Content-Type-Options"]="nosniff";ctx.Response.Headers["Referrer-Policy"]="same-origin";
    ctx.Response.Headers["X-Frame-Options"]="DENY";
    if(ctx.Request.Path.StartsWithSegments("/api"))ctx.Response.Headers.CacheControl="no-store";
    try{await next();}
    catch(ApiError e){ctx.Response.StatusCode=e.Status;await ctx.Response.WriteAsJsonAsync(new{error=e.Message});}
    catch(AntiforgeryValidationException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error="Your session changed. Refresh the page and try again."});}
    catch(InvalidDataException e){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error=e.Message});}
    catch(DbUpdateConcurrencyException){ctx.Response.StatusCode=409;await ctx.Response.WriteAsJsonAsync(new{error="This item changed. Refresh and try again."});}
});
app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
app.Use(async(ctx,next)=>{
    if(!ctx.Request.Path.StartsWithSegments("/api")){await next();return;}
    if(ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);
    if(ctx.Request.Path.StartsWithSegments("/api/events")){await next();return;}
    // Searches have their own bounded process pool and only read the account.
    // Never hold the multiplayer write lock while waiting for the engine.
    if(ctx.Request.Path=="/api/engine/search"){await next();return;}
    var gate=ctx.RequestServices.GetRequiredService<WriteGate>();await gate.Lock.WaitAsync(ctx.RequestAborted);
    try{
        if(ctx.User.Identity?.IsAuthenticated==true&&ctx.Request.Path!="/api/auth/logout"){var id=ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);var user=await ctx.RequestServices.GetRequiredService<PlatformDb>().Users.FindAsync(id);if(user==null||user.Suspended)throw new ApiError(403,"This account is unavailable.");}
        await next();
    }finally{gate.Lock.Release();}
});
app.MapGet("/health",()=>Results.Ok(new{status="ok"}));
app.MapCommunity(discord);
app.MapPost("/api/engine/search",async(SkynetZero.Web.EngineRequest request,HttpContext context,PlatformDb db,NativeSearch search)=>{
    if((await Api.User(context,db)).Suspended)throw new ApiError(403,"This account is unavailable.");
    return Results.Content(await search.Run(request,context.RequestAborted),"application/json");
}).RequireAuthorization().RequireRateLimiting("search");
app.UseDefaultFiles();app.UseStaticFiles(new StaticFileOptions{OnPrepareResponse=c=>{if(c.File.Name.EndsWith(".wasm"))c.Context.Response.ContentType="application/wasm";}});
app.MapFallback(async ctx=>{
    if(ctx.Request.Path.StartsWithSegments("/api")){ctx.Response.StatusCode=404;await ctx.Response.WriteAsJsonAsync(new{error="Endpoint not found."});return;}
    string index=Path.Combine(app.Environment.WebRootPath??Path.Combine(app.Environment.ContentRootPath,"wwwroot"),"index.html");
    if(File.Exists(index)){ctx.Response.ContentType="text/html";await ctx.Response.SendFileAsync(index);}else{ctx.Response.StatusCode=503;await ctx.Response.WriteAsync("Publish the web client to server/wwwroot to open the site.");}
});
await app.RunAsync();
public partial class Program;
