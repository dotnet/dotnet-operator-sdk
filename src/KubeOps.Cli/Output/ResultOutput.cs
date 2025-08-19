// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text;

using k8s;

using Spectre.Console;

namespace KubeOps.Cli.Output;

internal class ResultOutput(IAnsiConsole console, OutputFormat defaultFormat)
{
    private readonly Dictionary<string, (object, OutputFormat)> _files = new();

    public IEnumerable<string> Files => _files.Keys;

    public IEnumerable<string> DefaultFormatFiles =>
        _files
            .Where(f => f.Value.Item2 == defaultFormat)
            .Select(f => f.Key);

    public object this[string filename]
    {
        get => _files[filename].Item1;
    }

    public void Add(string filename, object content) => _files.Add(filename, (content, defaultFormat));

    public void Add(string filename, object content, OutputFormat format) => _files.Add(filename, (content, format));

    public async Task Write(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (var (filename, content) in _files)
        {
            await using var file = File.Open(
                Path.Join(
                    outputDirectory,
                    filename),
                FileMode.Create);
            await file.WriteAsync(Encoding.UTF8.GetBytes(Serialize(content)));
        }
    }

    public void Write()
    {
        console.Write(new Rule());
        foreach (var (filename, content) in _files)
        {
            console.MarkupLineInterpolated($"[bold]File:[/] [underline]{filename}[/]");
            console.WriteLine(Serialize(content));
            console.Write(new Rule());
        }
    }

    private static string Serialize((object Object, OutputFormat Format) data) => data.Format switch
    {
        OutputFormat.Yaml => KubernetesYaml.Serialize(data.Object),
        OutputFormat.Json => KubernetesJson.Serialize(data.Object),
        OutputFormat.Plain => data.Object.ToString() ?? string.Empty,
        _ => throw new ArgumentException("Unknown output format."),
    };
}
