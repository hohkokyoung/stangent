// Playwright for stangent's reproductions on Sonarr (D76, D86): the app is started and seeded by stangent (tickets.app).
// tests/ holds the two seeded specs; stangent/ is where reproductions go.
import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: '.',
  testMatch: ['tests/**/*.spec.ts', 'stangent/**/*.spec.ts', 'stangent/**/*.spec.js'],
  retries: 0,
  timeout: 30_000,
  use: { baseURL: process.env.BASE_URL ?? 'http://127.0.0.1:8989/', headless: true },
});
