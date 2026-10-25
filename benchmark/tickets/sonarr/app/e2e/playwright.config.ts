// Playwright for stangent's reproductions on Sonarr (D76): the app is started and seeded by stangent (tickets.app).
import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './stangent',
  retries: 0,
  timeout: 30_000,
  use: { baseURL: process.env.BASE_URL ?? 'http://127.0.0.1:8989/', headless: true },
});
