using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

// usage: shot <url> <out.png> <width> <height>
static class P { [STAThread] static void Main(string[] args) { Run(args); }
static void Run(string[] args) {
var url = args[0]; var outPath = args[1]; var w = int.Parse(args[2]); var h = int.Parse(args[3]); var transparent = args.Length > 4 && args[4] == "transparent";
Application.SetHighDpiMode(HighDpiMode.SystemAware);
Application.EnableVisualStyles();
var form = new Form { Width = w + 40, Height = h + 80, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, 0) };
var view = new WebView2 { Dock = DockStyle.Fill };
form.Controls.Add(view);
form.Shown += async (_, _) =>
{
    try
    {
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "shot-wv2"));
        await view.EnsureCoreWebView2Async(env);
        var done = new TaskCompletionSource();
        view.CoreWebView2.NavigationCompleted += (_, _) => done.TrySetResult();
        view.CoreWebView2.Navigate(url);
        await done.Task;
        await Task.Delay(1500);
        // draw at exactly w x h CSS pixels regardless of the window's DPI
        await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", $"{{\"width\":{w},\"height\":{h},\"deviceScaleFactor\":1,\"mobile\":false}}");
        if (transparent) await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDefaultBackgroundColorOverride", "{\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0}}");
        await Task.Delay(500);
        var frames = int.Parse(Environment.GetEnvironmentVariable("FRAMES") ?? "0");
        for (var i = 0; i < Math.Max(1, frames); i++)
        {
            if (frames > 0)
            {
                // an animated page exposes window.__setT(t), t from 0 to 1, so every frame is exactly reproducible
                await view.CoreWebView2.ExecuteScriptAsync($"window.__setT({(i / (double)frames).ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                await Task.Delay(60);
            }
            var json = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", $"{{\"format\":\"png\",\"clip\":{{\"x\":0,\"y\":0,\"width\":{w},\"height\":{h},\"scale\":1}}}}");
            var b64 = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("data").GetString()!;
            var target = frames > 0 ? outPath.Replace(".png", $"-{i:000}.png") : outPath;
            File.WriteAllBytes(target, Convert.FromBase64String(b64));
        }
    }
    catch (Exception ex) { File.WriteAllText(outPath + ".err", ex.ToString()); }
    Application.Exit();
};
Application.Run(form);
} }
