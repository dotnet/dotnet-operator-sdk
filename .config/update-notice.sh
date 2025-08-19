#!/bin/bash
dotnet nuget-license -ji .config/nuget-license-input.json -ignore .config/nuget-license-ignore.json -o Markdown > NOTICE.md
