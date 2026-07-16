const isStableRelease = process.env.SEMANTIC_RELEASE_STABLE === "true";

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
