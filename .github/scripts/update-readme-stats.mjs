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
const root = new URL('../../', import.meta.url);

const response = JSON.parse(execFileSync('gh', ['api', '--hostname', 'github.com', 'graphql', '-f', `query=${query}`], { encoding: 'utf8' }));
const repo = response.data?.repository;
const counts = [repo?.stargazerCount, repo?.forkCount, repo?.issues?.totalCount, repo?.pullRequests?.totalCount];
if (response.errors || !counts.every(value => Number.isSafeInteger(value) && value >= 0)) {
  throw new Error('GitHub returned incomplete repository statistics.');
}

const file = new URL('assets/readme-banner.svg', root);
const source = readFileSync(file, 'utf8');
const start = source.indexOf(begin);
const stop = source.indexOf(end);
if (start === -1 || stop <= start || start !== source.lastIndexOf(begin) || stop !== source.lastIndexOf(end)) {
  throw new Error('Expected one ordered pair of statistics markers in the README banner.');
}

const badges = [
  ['STARS', 106, '#dfb317'],
  ['FORKS', 110, '#97ca00'],
  ['ISSUES', 116, '#007ec6'],
  ['PULLS', 106, '#fe7d37'],
].map(([label, labelWidth, color], index) => {
  const value = String(counts[index]);
  return { label, labelWidth, color, value, valueWidth: 30 + 14 * value.length };
});

const gap = 20;
const width = badges.reduce((sum, badge) => sum + badge.labelWidth + badge.valueWidth, gap * (badges.length - 1));
let x = (1280 - width) / 2;
const groups = badges.map(({ label, labelWidth, color, value, valueWidth }) => {
  const group = `    <g transform="translate(${x} 502)">
      <rect width="${labelWidth}" height="50" fill="#555"/>
      <rect x="${labelWidth}" width="${valueWidth}" height="50" fill="${color}"/>
      <text x="${labelWidth / 2}" y="32">${label}</text>
      <text x="${labelWidth + valueWidth / 2}" y="32">${value}</text>
    </g>`;
  x += labelWidth + valueWidth + gap;
  return group;
});

const summary = badges.map(({ label, value }) => label + ' ' + value).join(' · ');
const previous = source.slice(start + begin.length, stop);
const previousDate = previous.match(/<title id="banner-title">Star-Pie\/StarPie · (\d{4}-\d{2}-\d{2}) UTC · /)?.[1];
const date = previousDate && previous.includes(summary + '</title>') ? previousDate : new Date().toISOString().slice(0, 10);
const block = [
  '',
  `  <title id="banner-title">Star-Pie/StarPie · ${date} UTC · ${summary}</title>`,
  '  <g font-family="Arial, sans-serif" font-size="20" letter-spacing="1.5" fill="white" text-anchor="middle">',
  ...groups,
  '  </g>',
  '  ',
].join(source.includes('\r\n') ? '\r\n' : '\n');
if (block !== previous) writeFileSync(file, source.slice(0, start + begin.length) + block + source.slice(stop));
