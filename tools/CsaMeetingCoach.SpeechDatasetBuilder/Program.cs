using System.Text;
using CsaMeetingCoach.Core;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine(
        "Usage: CsaMeetingCoach.SpeechDatasetBuilder <output-path>");
    return 2;
}

var outputPath = Path.GetFullPath(args[0]);
var parent = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrEmpty(parent))
{
    Directory.CreateDirectory(parent);
}

await File.WriteAllLinesAsync(
    outputPath,
    CustomSpeechLanguageCorpusBuilder.Build(),
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
Console.WriteLine($"Wrote Custom Speech language corpus to {outputPath}.");
return 0;
