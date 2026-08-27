using System.Security.Cryptography;

namespace Jarvis.Speech;

public static class SpeechModelInstaller
{
    public static async Task<string> EnsureSileroModelAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken) =>
        await EnsureAssetAsync(
            httpClient,
            SpeechModelCatalog.SileroDownloadUri,
            SpeechModelCatalog.GetSileroModelPath(),
            SpeechModelCatalog.SileroSha256,
            cancellationToken).ConfigureAwait(false);

    public static async Task<string> EnsureWhisperModelAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken) =>
        await EnsureAssetAsync(
            httpClient,
            SpeechModelCatalog.WhisperDownloadUri,
            SpeechModelCatalog.GetWhisperModelPath(),
            SpeechModelCatalog.WhisperSha256,
            cancellationToken).ConfigureAwait(false);

    private static async Task<string> EnsureAssetAsync(
        HttpClient httpClient,
        Uri downloadUri,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destination) &&
            await HasExpectedHashAsync(destination, expectedSha256, cancellationToken))
        {
            return destination;
        }

        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The Silero model directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var temporary = destination + ".download";

        try
        {
            await using (var output = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,
                useAsync: true))
            await using (var input = await httpClient.GetStreamAsync(
                downloadUri,
                cancellationToken))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            if (!await HasExpectedHashAsync(temporary, expectedSha256, cancellationToken))
            {
                throw new InvalidDataException("The downloaded speech model failed SHA-256 verification.");
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash) == expectedSha256;
    }
}
