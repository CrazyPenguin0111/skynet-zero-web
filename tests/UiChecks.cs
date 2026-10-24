using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SkynetZero.Web;
internal static class UiChecks
{
    public static async Task Run(PositionView position,Evaluation evaluation)
    {
        var collection=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime,NoJs>();
        collection.AddSingleton<IComponentActivator>(new FixtureActivator(position,evaluation));
        using var services=collection.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=> {
            var result=await renderer.RenderComponentAsync<App>();string html=result.ToHtmlString();
            foreach(string expected in new[]{"SKYNET","Analysis board","White ACPL","Export 5DPGN","role=\"gridcell\"","M1"})
                if(!html.Contains(expected))throw new Exception("Rendered Blazor UI is missing "+expected);
            if(html.Contains("Microsoft.AspNetCore.Components.RenderFragment"))throw new Exception("Broken Razor component binding");
        });
        Console.WriteLine("PASS Blazor render: multiverse boards, analysis, mate score and export controls");
    }
    private sealed class FixtureActivator(PositionView position,Evaluation evaluation):IComponentActivator {
        public IComponent CreateInstance(Type type) {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is App) {
                void Set(string field,object value)=>typeof(App).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(component,value);
                Set("_position",position);Set("_review",true);Set("_viewIndex",evaluation.Index);
                var values=(Dictionary<int,Evaluation>)typeof(App).GetField("_evaluations",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(component)!;values[evaluation.Index]=evaluation;
            }
            return component;
        }
    }
    private sealed class NoJs:IJSRuntime {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args)=>ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken token,object?[]? args)=>ValueTask.FromResult(default(TValue)!);
    }
}
