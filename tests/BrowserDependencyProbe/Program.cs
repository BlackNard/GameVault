using System.Reflection;
using System.Runtime.Loader;

// Load only the clean published graph. No WPF control, browser, window, app workspace or profile is created.
if(args.Length!=1){Console.Error.WriteLine("Expected a clean publish directory.");return 2;}
var root=Path.GetFullPath(args[0]);
var context=new PublishedGraph(root);
try
{
    foreach(var name in new[]{"Microsoft.Windows.SDK.NET","WinRT.Runtime"})
        if(!File.Exists(Path.Combine(root,name+".dll")))throw new FileNotFoundException("Published browser dependency is missing: "+name);
    var app=context.LoadFromAssemblyPath(Path.Combine(root,"GameVault.dll"));
    var check=app.GetType("GameVault.App.Artwork.BrowserDependencies",throwOnError:true)!.GetMethod("Verify",BindingFlags.Public|BindingFlags.Static)!;
    check.Invoke(null,null);
    Console.WriteLine("Published browser composition dependencies resolve without opening a browser or window.");
    return 0;
}
catch(Exception error)
{
    Console.Error.WriteLine("Published browser dependency check failed: "+(error is TargetInvocationException wrapped ? wrapped.InnerException?.GetType().Name : error.GetType().Name));
    return 1;
}
finally{context.Unload();}
sealed class PublishedGraph(string root):AssemblyLoadContext(isCollectible:true)
{
    protected override Assembly? Load(AssemblyName name)
    {
        if(name.Name is "Microsoft.Windows.SDK.NET" or "WinRT.Runtime")
            return LoadFromAssemblyPath(Path.Combine(root,name.Name+".dll"));
        var path=Path.Combine(root,name.Name+".dll");
        return File.Exists(path)?LoadFromAssemblyPath(path):null;
    }
}
