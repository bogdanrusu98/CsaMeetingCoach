using CsaMeetingCoach.Api;

if (args.Length != 3 || !OperatingSystem.IsWindows())
{
    return 64;
}

try
{
    using var startEvent = EventWaitHandle.OpenExisting(args[2]);
    if (!startEvent.WaitOne(TimeSpan.FromSeconds(10)))
    {
        return 3;
    }

    var content = PdfTextExtractionEngine.Extract(
        args[0],
        CancellationToken.None);
    await File.WriteAllTextAsync(args[1], content);
    return 0;
}
catch (PdfExtractionRejectedException)
{
    return 2;
}
catch
{
    return 3;
}
