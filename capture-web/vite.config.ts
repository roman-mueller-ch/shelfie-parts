import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In development the API calls go to the deployed capture API over Tailscale,
// so no local secrets are needed. Set SHELFIE_API to use `dotnet run` instead.
const api = process.env.SHELFIE_API ?? "https://capture.tailf98c37.ts.net";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { "/api": { target: api, changeOrigin: true } },
  },
});
