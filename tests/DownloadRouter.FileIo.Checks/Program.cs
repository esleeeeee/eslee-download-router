using System.Security.Cryptography;
using System.Text.Json;
using DownloadRouter.Core.Paths;
using DownloadRouter.Infrastructure.Files;
using Microsoft.Extensions.Logging.Abstractions;

if (args.Length != 1) throw new ArgumentException("Pass a separate local fixed-volume root, e.g. D:\\");
var destinationVolume = Path.GetPathRoot(Path.GetFullPath(args[0])) ?? throw new ArgumentException("Volume root required.");
if (!string.Equals(Path.GetFullPath(args[0]), destinationVolume, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Pass the volume root only.");
var drive = new DriveInfo(destinationVolume);
if (drive.DriveType != DriveType.Fixed || !drive.IsReady) throw new IOException("A ready local fixed volume is required.");
var suffix = "download-router-volume-check-" + Guid.NewGuid().ToString("N");
var sourceRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), suffix));
var destinationRoot = Path.GetFullPath(Path.Combine(destinationVolume, suffix));
if (string.Equals(Path.GetPathRoot(sourceRoot), destinationVolume, StringComparison.OrdinalIgnoreCase)) throw new IOException("Two distinct volumes are required.");
if (drive.AvailableFreeSpace < 1024L * 1024 * 1024) throw new IOException("Insufficient destination free space.");
Directory.CreateDirectory(sourceRoot); Directory.CreateDirectory(destinationRoot);
try
{
    var source = Path.Combine(sourceRoot, "source.bin");
    var block = new byte[1024 * 1024]; new Random(729).NextBytes(block);
    using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    {
        for (var index = 0; index < 128; index++) { block[0] = (byte)index; stream.Write(block); }
        stream.Flush(true);
    }
    string expected;
    using (var stream = File.OpenRead(source)) expected = Convert.ToHexString(SHA256.HashData(stream));
    var service = new FileMoveService(new PathBoundaryValidator(), NullLogger<FileMoveService>.Instance);
    FileMoveResult pending;
    // A real Windows sharing lock allows reading/copying but denies source deletion.
    using (var readLock = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        pending = await service.MoveAsync(source, destinationRoot, null, true);
    Require(!pending.Success && pending.CanRetry && pending.ErrorCode == "file.source-cleanup-pending", "OS delete lock must leave a recoverable published destination.");
    Require(File.Exists(source) && File.Exists(pending.DestinationPath), "Both files must remain until cleanup retry.");
    var journal = Directory.GetFiles(destinationRoot, ".eslee-move-*.json").Single();
    Require(File.Exists(journal), "Publication must be journaled.");
    var restarted = new FileMoveService(new PathBoundaryValidator(), NullLogger<FileMoveService>.Instance);
    var completed = await restarted.MoveAsync(source, destinationRoot, null, true);
    Require(completed.Success && completed.DestinationPath == pending.DestinationPath, "Restart must reuse verified published destination.");
    Require(!File.Exists(source), "Source should be removed only after its lock is released.");
    Require(Directory.GetFiles(destinationRoot).Length == 1, "No duplicate or journal/temp leak.");
    using var final = File.OpenRead(completed.DestinationPath!);
    var actual = Convert.ToHexString(SHA256.HashData(final));
    Require(expected == actual, "Actual cross-volume bytes must retain SHA256.");
    Console.WriteLine("PASS actual C-to-other-volume copy/hash/publication, OS source-delete lock, restart cleanup and one destination");
    Console.WriteLine(JsonSerializer.Serialize(new { sourceVolume = Path.GetPathRoot(sourceRoot), destinationVolume, bytes = final.Length, sourceSha256 = expected, destinationSha256 = actual, pending.ErrorCode }));
}
finally
{
    // Both roots were generated above, are absolute, and are confined to the named test namespace.
    if (Path.GetFileName(sourceRoot) != suffix || Path.GetFileName(destinationRoot) != suffix) throw new IOException("Cleanup boundary mismatch.");
    Directory.Delete(sourceRoot, true); Directory.Delete(destinationRoot, true);
    Console.WriteLine("CLEANUP both generated volume test directories removed");
}
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
