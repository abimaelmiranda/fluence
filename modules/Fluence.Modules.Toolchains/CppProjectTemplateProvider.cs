using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Projects;

namespace Fluence.Modules.Toolchains;

public sealed class CppProjectTemplateProvider(IFileService files) : IProjectTemplateProvider
{
    public string ProviderId => "cpp";

    public string DisplayName => "C / C++";

    public bool SupportsSolutionCreation => false;

    public IReadOnlyList<ProjectTemplateDefinition> Templates { get; } =
    [
        new("c-console", "C Console App", "A C console application"),
        new("cpp-console", "C++ Console App", "A C++ console application"),
        new("c-cmake", "C CMake Project", "A CMake project configured for C"),
        new("cpp-cmake", "C++ CMake Project", "A CMake project configured for C++"),
    ];

    public bool CanHandleWorkspace(string? languageId) =>
        languageId is "c" or "cpp";

    public Task<ProjectCreationResult> CreateAsync(
        ProjectCreationRequest request,
        Action<string> onOutput,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var projectRoot = Path.Combine(request.Location, request.ProjectName);
        Directory.CreateDirectory(projectRoot);

        var useCpp = request.Template.Id.Contains("cpp", StringComparison.OrdinalIgnoreCase);
        var sourceFile = useCpp ? Path.Combine(projectRoot, "src", "main.cpp") : Path.Combine(projectRoot, "src", "main.c");
        var sourceDir = Path.GetDirectoryName(sourceFile)!;
        files.CreateDirectory(sourceDir);

        var cmakeLists = BuildCMakeLists(request.ProjectName, useCpp, request.Template.Id.Contains("console", StringComparison.OrdinalIgnoreCase));
        var mainFile = useCpp ? BuildCppMain() : BuildCMain();
        var gitignore = BuildGitIgnore();

        onOutput($"[cpp] Creating {request.Template.Name} at {projectRoot}");
        files.WriteText(Path.Combine(projectRoot, "CMakeLists.txt"), cmakeLists);
        files.WriteText(sourceFile, mainFile);
        files.WriteText(Path.Combine(projectRoot, ".gitignore"), gitignore);

        return Task.FromResult(new ProjectCreationResult(projectRoot, null));
    }

    private static string BuildCMakeLists(string projectName, bool useCpp, bool isConsoleApp)
    {
        var language = useCpp ? "CXX" : "C";
        var standard = useCpp ? "17" : "11";
        var source = useCpp ? "src/main.cpp" : "src/main.c";
        var addExecutable = isConsoleApp
            ? $"add_executable({projectName} {source})"
            : $"add_library({projectName} {source})";

        return $$"""
cmake_minimum_required(VERSION 3.20)
project({{projectName}} LANGUAGES {{language}})

set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
set(CMAKE_{{language}}_STANDARD {{standard}})
set(CMAKE_{{language}}_STANDARD_REQUIRED ON)

{{addExecutable}}
""";
    }

    private static string BuildCMain() =>
        """
#include <stdio.h>

int main(void) {
    printf("Hello from C\n");
    return 0;
}
""";

    private static string BuildCppMain() =>
        """
#include <iostream>

int main() {
    std::cout << "Hello from C++\n";
    return 0;
}
""";

    private static string BuildGitIgnore() =>
        """
build/
out/
cmake-build-*/
compile_commands.json
""";
}
