const isStableRelease = process.env.SEMANTIC_RELEASE_STABLE === "true";
const migrationGuideUrl = "https://dotnet.github.io/dotnet-operator-sdk/docs/operator/migration";

// Every release with breaking changes links the migration guide section of its major version.
const linkMigrationGuide = (context) => {
  const breakingChanges = context.noteGroups?.find((group) => group.title === "BREAKING CHANGES");
  const major = Number.parseInt(context.version, 10);

  if (breakingChanges && major > 0) {
    const section = `upgrading-from-v${major - 1}-to-v${major}`;
    breakingChanges.notes.unshift({
      text: `See the [migration guide](${migrationGuideUrl}#${section}) for upgrading from v${major - 1} to v${major}.`,
    });
  }

  return context;
};

export default {
  debug: true,
  plugins: [
    [
      "@semantic-release/commit-analyzer",
      {
        preset: "conventionalcommits",
        releaseRules: [{ type: "deps", scope: "core", release: "patch" }],
      },
    ],
    [
      "@semantic-release/release-notes-generator",
      {
        preset: "conventionalcommits",
        presetConfig: {
          types: [
            { type: "feat", section: "Features" },
            { type: "fix", section: "Bug Fixes" },
            { type: "perf", section: "Performance Improvements" },
            { type: "deps", section: "Dependencies" },
            { type: "revert", section: "Reverts" },
            { type: "docs", section: "Documentation" },
            { type: "style", section: "Styles", hidden: true },
            { type: "chore", section: "Miscellaneous Chores", hidden: true },
            { type: "refactor", section: "Code Refactoring", hidden: true },
            { type: "test", section: "Tests", hidden: true },
            { type: "build", section: "Build System", hidden: true },
            { type: "ci", section: "Continuous Integration", hidden: true },
          ],
        },
        writerOpts: {
          finalizeContext: linkMigrationGuide,
        },
      },
    ],
    [
      "semantic-release-net",
      {
        sources: [
          {
            url: "https://api.nuget.org/v3/index.json",
            apiKeyEnvVar: "NUGET_API_KEY",
          },
        ],
      },
    ],
    [
      "@semantic-release/github",
      {
        successComment: isStableRelease ? undefined : false,
        successCommentCondition: isStableRelease
          ? "<% return Boolean(issue.pull_request); %>"
          : false,
        failComment: false,
        releasedLabels: isStableRelease ? ["released"] : false,
        assets: [
          {
            path: "src/**/bin/Release/**/*.nupkg",
          },
        ],
      },
    ],
  ],
};
