using System.IO;
using System.Security.Cryptography;
using WinPieGestures.Plugins;

if (args.Length != 4) throw new ArgumentException("host-dll host-sha plugin-dll plugin-sha required");
static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
if (Hash(typeof(PluginHost).Assembly.Location) != args[1] || Hash(args[0]) != args[1] || Hash(args[2]) != args[3])
    throw new InvalidOperationException("Candidate hash mismatch");
PluginScanResult scan = PluginScanner.ScanSelectedDll(args[2]);
if (!scan.Accepted) throw new InvalidOperationException(scan.DescribeFailure());
if (scan.Manifest!.Id != "local.temporarykeyboard.preview" || scan.Manifest.ApiVersion != "1.15")
    throw new InvalidOperationException("Preview manifest mismatch");
Console.WriteLine($"PASS static PE/manifest scan: {scan.Manifest.Id}; host={args[1]}; plugin={args[3]}");
Console.WriteLine("No installation, plugin activation, window, input or action execution.");
