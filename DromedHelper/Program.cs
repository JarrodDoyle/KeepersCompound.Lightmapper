using System.Diagnostics;

// Console.WriteLine("Attempting to open DromEd.log");
// Thread.Sleep(2000);

try
{
    using var fs = new FileStream("DromEd.log", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var sr = new StreamReader(fs);

    // Console.WriteLine("Opened dromed.log");
    while (!sr.EndOfStream)
    {
        // Console.WriteLine("Reading line...");
    
        var line = sr.ReadLine();
        if (line != null && line.StartsWith(": FM Path: "))
        {
            // Console.WriteLine($"Found FM Line: {line}");
            var campaignName = line[11..].Split(@"\").Last();
            // Console.WriteLine($"Campaign name: {campaignName}");
            // Console.WriteLine("Attempting to light...");
            var process = Process.Start(@"Tools\KCTools\KCTools.exe", $"light . KCLight.cow -c \"{campaignName}\"");
            process.WaitForExit();
            // Console.WriteLine("Should be lit!");
            break;
        }
    }
}
catch (Exception e)
{
    // Console.WriteLine("Uh oh something went wrong. Writing to DromedHelper.log");
    using var outputFile = new StreamWriter(@"Tools\KCTools\DromedHelper.log");
    outputFile.WriteLine(e);
}

Thread.Sleep(2000);
