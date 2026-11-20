import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';

const query = `query {
  repository(owner: "Star-Pie", name: "StarPie") {
    stargazerCount
    forkCount
    issues(states: OPEN) { totalCount }
    pullRequests(states: OPEN) { totalCount }
  }
}`;
const begin = '<!-- github-stats:start -->';
const end = '<!-- github-stats:end -->';
const base = 'https://github.com/Star-Pie/StarPie';
const root = new URL('../../', import.meta.url);

const response = JSON.parse(execFileSync('gh', ['api', '--hostname', 'github.com', 'graphql', '-f', `query=${query}`], { encoding: 'utf8' }));
const repo = response.data?.repository;
const counts = [repo?.stargazerCount, repo?.forkCount, repo?.issues?.totalCount, repo?.pullRequests?.totalCount];
if (response.errors || !counts.every(value => Number.isSafeInteger(value) && value >= 0)) {
  throw new Error('GitHub returned incomplete repository statistics.');
}

const docs = [
  ['README.md', ['Stars', 'Forks', '开放议题', '开放 PR']],
  ['README_EN.md', ['Stars', 'Forks', 'Open issues', 'Open PRs']],
];
const paths = ['stargazers', 'forks', 'issues', 'pulls'];
const updates = docs.map(([name, labels]) => {
  const file = new URL(name, root);
  const source = readFileSync(file, 'utf8');
  if (source.split(begin).length !== 2 || source.split(end).length !== 2) {
    throw new Error(`Expected exactly one statistics block in ${name}.`);
  }
  const from = source.indexOf(begin) + begin.length;
  const to = source.indexOf(end);
  if (to < from) throw new Error(`Reversed statistics markers in ${name}.`);
  const nl = source.includes('\r\n') ? '\r\n' : '\n';
  const links = labels.map((label, i) => `[${label} ${counts[i]}](${base}/${paths[i]})`);
  const current = source.slice(from, to);
  const row = links.join(' · ');
  const label = name === 'README.md' ? 'GitHub 统计快照' : 'GitHub snapshot';
  const previousDate = current.match(/\((\d{4}-\d{2}-\d{2}) UTC\)/)?.[1];
  const date = previousDate && current.includes(row) ? previousDate : new Date().toISOString().slice(0, 10);
  const snapshot = `${nl}${label} (${date} UTC) · ${row}${nl}`;
  return { file, source, result: source.slice(0, from) + snapshot + source.slice(to) };
});

// Validate both markers before changing either language.
for (const { file, source, result } of updates) {
  if (source !== result) writeFileSync(file, result);
}
