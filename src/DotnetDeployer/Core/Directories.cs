using System.IO;

namespace DotnetDeployer.Core;

public static class Directories
{
    public static string Temp => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DotnetDeployer");

    public static string GetTempPath()
    {
        if (!Directory.Exists(Temp))
        {
            Directory.CreateDirectory(Temp);
        }
        return Temp;
    }

    public static string GetTempFileName()
    {
        var tempPath = GetTempPath();
        var fileName = System.IO.Path.GetRandomFileName();
        var fullPath = System.IO.Path.Combine(tempPath, fileName);
        
        using (File.Create(fullPath)) { }
        
        return fullPath;
    }
}
