import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// The portal is served by BlueID.Web under /portal/; the API (/me/v1, /admin/v1) is another process behind the same
// origin (Caddy in dev and production). The dev server proxies both so cookies stay first-party.
const web = process.env.BID_DEV_WEB ?? "http://localhost:5090";
const api = process.env.BID_DEV_API ?? "http://localhost:5091";
export default defineConfig({
  base: "/portal/",
  plugins: [react()],
  server: {
    port: 5174,
    proxy: {
      "/me/v1": { target: api },
      "/admin/v1": { target: api },
      "/login": { target: web },
      "/logout": { target: web },
      "/css": { target: web },
    },
  },
  build: { outDir: "../src/BlueID.Web/wwwroot/portal", emptyOutDir: true, sourcemap: true, target: "es2022" },
  test: { environment: "jsdom", include: ["src/**/*.test.ts", "src/**/*.test.tsx"] },
});
