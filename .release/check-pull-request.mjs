// Evaluates a pull request with the semantic-release configuration of release.base.mjs, so the
// PR checks and the release share one set of rules. PRs are squash-merged: the title becomes the
// commit header and the description the commit body.
import { appendFileSync } from "node:fs";
import { analyzeCommits } from "@semantic-release/commit-analyzer";
import createPreset from "conventional-changelog-conventionalcommits";
import { CommitParser } from "conventional-commits-parser";
import config from "./release.base.mjs";

const pluginConfig = (name) => config.plugins.find(([plugin]) => plugin === name)[1];
const analyzer = pluginConfig("@semantic-release/commit-analyzer");
const { presetConfig } = pluginConfig("@semantic-release/release-notes-generator");

const { PR_TITLE: title = "", PR_BODY: body = "", PR_NUMBER: number } = process.env;

const preset = await createPreset(presetConfig);
const { type } = new CommitParser({ ...preset.parser, ...analyzer.parserOpts }).parse(title);
const types = presetConfig.types.map((entry) => entry.type);

if (!types.includes(type)) {
  console.log(
    `::error::The PR title must follow Conventional Commits with one of the types ${types.join(", ")}, ` +
      `e.g. "feat(core): add a setting" or "feat(core)!: remove a setting".`,
  );
  process.exit(1);
}

const release = await analyzeCommits(analyzer, {
  cwd: import.meta.dirname,
  commits: [{ hash: "0".repeat(40), message: `${title} (#${number})\n\n${body}` }],
  logger: { log: () => {} },
});

console.log(`This pull request results in: ${release ?? "no"} release`);
appendFileSync(process.env.GITHUB_STEP_SUMMARY, `This pull request results in: **${release ?? "no"} release**\n`);
appendFileSync(process.env.GITHUB_OUTPUT, `breaking=${release === "major"}\n`);
