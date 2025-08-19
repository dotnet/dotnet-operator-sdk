// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using KubeOps.Cli.Output;

namespace KubeOps.Cli.Generators;

internal class DockerfileGenerator(bool hasWebhooks) : IConfigGenerator
{
    public void Generate(ResultOutput output)
    {
        output.Add(
            "Dockerfile",
            $"""
             FROM mcr.microsoft.com/dotnet/sdk:latest as build
             WORKDIR /operator

             COPY ./ ./
             RUN dotnet publish -c Release /p:AssemblyName=operator -o out

             # The runner for the application
             FROM mcr.microsoft.com/dotnet/{(hasWebhooks ? "aspnet" : "runtime")}:latest as final

             RUN addgroup k8s-operator && useradd -G k8s-operator operator-user

             WORKDIR /operator
             COPY --from=build /operator/out/ ./
             RUN chown operator-user:k8s-operator -R .

             USER operator-user

             ENTRYPOINT [ "dotnet", "operator.dll" ]
             """,
            OutputFormat.Plain);
    }
}
