import { test } from "node:test";
import assert from "node:assert/strict";
import { Reasons, readTestFile } from "../src/testSteps.ts";

const head = "import { test, expect } from '@playwright/test';\n";

function steps(body: string) {
  const file = readTestFile("a.spec.ts", `${head}test('t', async ({ page }) => {\n${body}\n});\n`);
  assert.equal(file.blocks.length, 1);
  return file.blocks[0].steps;
}

test("plain steps come in order, with their lines and the test's words", () => {
  const s = steps(`
  await page.goto('/orders');
  await page.getByRole('button', { name: 'New' }).click();
  await page.getByLabel('Name').fill('Ann');
  await page.locator('select').selectOption('A');
  await expect(page.getByText('Saved')).toBeVisible();`);
  assert.deepEqual(s.map((x) => x.kind), ["goto", "click", "fill", "select", "check"]);
  assert.deepEqual(s.map((x) => x.line), [4, 5, 6, 7, 8]);
  assert.equal(s[0].url, "/orders");
  assert.equal(s[0].receiver, "page");
  assert.equal(s[1].words, "page.getByRole('button', { name: 'New' }).click()");
  assert.equal(s[4].words, "expect(page.getByText('Saved')).toBeVisible()");
  assert.ok(s.every((x) => x.reason === undefined));
});

test("a helper is one ❓ step, and nothing it might do is invented", () => {
  const s = steps(`await emptyCart(page);\nawait page.goto('/cart');`);
  assert.deepEqual(s.map((x) => [x.kind, x.reason ?? null]), [["unknown", Reasons.helper], ["goto", null]]);
  assert.equal(s[0].words, "emptyCart(page)");
});

test("a page object's method is ❓, and its click keeps its verb but not a target", () => {
  const s = steps(`await home.openCart();\nawait home.buy.click();\nawait expect(home.title).toHaveText('x');`);
  assert.deepEqual(s.map((x) => [x.kind, x.reason]), [["unknown", Reasons.object], ["click", Reasons.objectTarget], ["check", Reasons.objectTarget]]);
});

test("a computed selector is ❓, whether built in place or held in a const", () => {
  const s = steps(`
  await page.locator(\`.row-\${id}\`).click();
  const row = page.locator(sel);
  await expect(row).toBeVisible();
  await page.getByRole('link', { name: who }).click();
  await page.locator('.a').nth(i).click();
  await page.click(sel);`);
  assert.equal(s.length, 5);
  assert.ok(s.every((x) => x.reason === Reasons.selector), JSON.stringify(s));
});

test("a const naming a place is followed, and isn't a step", () => {
  const s = steps(`const q = page.locator('.q');\nawait expect(q).toHaveValue('1');`);
  assert.equal(s.length, 1);
  assert.equal(s[0].target, "page.locator('.q')");
  assert.equal(s[0].reason, undefined);
});

test("a let isn't followed, since it can change", () => {
  const s = steps(`let q = page.locator('.q');\nawait expect(q).toHaveValue('1');`);
  assert.equal(s[s.length - 1].reason, Reasons.value);
});

test("loops and conditions are one ❓ each, with a count, and what's inside isn't listed", () => {
  const s = steps(`
  for (const x of xs) { await page.click(x); await page.click(x); }
  if (a) { await page.goto('/a'); }
  try { await page.goto('/b'); } catch { }
  a && (await page.goto('/c'));`);
  assert.deepEqual(s.map((x) => x.reason), [Reasons.loop, Reasons.condition, Reasons.tryBlock, Reasons.condition]);
  assert.equal(s[0].words, "for (const x of xs)");
  assert.equal(s[0].inside, 2);
  assert.ok(s.every((x) => x.kind === "unknown"));
});

test("a built URL has no url, and the goto stays a goto", () => {
  const s = steps("await page.goto(`/a/${id}`);\nawait page.goto('/a' + id);\nawait page.goto(url);\nawait admin.goto('/x');");
  assert.deepEqual(s.map((x) => [x.kind, x.url, x.receiver]), [["goto", null, "page"], ["goto", null, "page"], ["goto", null, "page"], ["goto", "/x", "admin"]]);
});

test("Promise.all's steps are read in the order written, and a held promise isn't read twice", () => {
  const s = steps(`
  const wait = page.waitForRequest(r => r.method() === 'POST');
  await Promise.all([wait, page.getByRole('button').first().click()]);`);
  assert.deepEqual(s.map((x) => x.kind), ["wait", "click"]);
  assert.deepEqual(s.map((x) => x.line), [4, 5]);
});

test("test.step is walked through in place", () => {
  const s = steps(`await test.step('open', async () => { await page.goto('/a'); });\nawait page.click('b');`);
  assert.deepEqual(s.map((x) => x.kind), ["goto", "click"]);
});

test("a value's check is ❓, and a read is kept as a read", () => {
  const s = steps(`const n = await page.locator('.a').count();\nexpect(n).toBe(3);`);
  assert.deepEqual(s.map((x) => [x.kind, x.reason ?? null]), [["read", null], ["check", Reasons.value]]);
});

test("a page call nobody knows is ❓; opening and closing a browser isn't a step", () => {
  const s = steps(`const p = await browser.newPage();\nawait page.evaluate(() => 1);\nawait page.close();`);
  assert.deepEqual(s.map((x) => [x.kind, x.reason]), [["unknown", Reasons.pageCall]]);
});

test("a value set up with no page and no await isn't a step", () => {
  assert.deepEqual(steps(`const name = makeName();\nconst o = new Home(page);\nconsole.log(name);`), []);
});

test("describes give titles, hooks are blocks of their own, and test.skip is said", () => {
  const f = readTestFile("a.spec.ts", `${head}
test.describe('orders', () => {
  test.beforeEach(async ({ page }) => { await page.goto('/orders'); });
  test('one', async ({ page }) => { await page.click('a'); });
  test.skip('two', async ({ page }) => { await page.click('b'); });
});`);
  assert.deepEqual(f.blocks.map((b) => [b.kind, b.titles, b.mode, b.steps.length]), [
    ["beforeEach", ["orders"], null, 1], ["test", ["orders", "one"], null, 1], ["test", ["orders", "two"], "skip", 1],
  ]);
  assert.equal(f.pageSteps, 3);
});

test("a body that's elsewhere is ❓", () => {
  const f = readTestFile("a.spec.ts", `${head}test('t', run);`);
  assert.equal(f.blocks[0].steps[0].reason, Reasons.callback);
});

test("tests made in a loop or by a call are unread, not guessed", () => {
  const f = readTestFile("a.spec.ts", `${head}for (const x of xs) { test(x, async () => {}); }\ndefineTests();\n`);
  assert.equal(f.blocks.length, 0);
  assert.equal(f.unread.length, 2);
  assert.match(f.unread[0].reason, /loop/);
  assert.match(f.unread[1].reason, /call/);
});

test("options outside a test are nothing", () => {
  const f = readTestFile("a.spec.ts", `${head}test.use({ a: 1 });\ntest.describe.configure({ mode: 'serial' });\ntest.setTimeout(5);\n`);
  assert.deepEqual([f.blocks, f.unread], [[], []]);
});

test("another runner's test has no page steps, so it can be told apart", () => {
  const f = readTestFile("a.test.ts", "test('adds', () => { expect(add(1, 1)).toBe(2); });");
  assert.equal(f.pageSteps, 0);
});

test("a broken file says so", () => {
  const f = readTestFile("a.spec.ts", `${head}test('t', async ({ page }) => { await page.goto('/a'); `);
  assert.ok(f.unread.some((u) => /syntax/.test(u.reason)));
});

test("the same text gives the same bytes", () => {
  const text = `${head}test('t', async ({ page }) => { await helper(page); await page.goto('/a'); });`;
  assert.equal(JSON.stringify(readTestFile("a.spec.ts", text)), JSON.stringify(readTestFile("a.spec.ts", text)));
});
