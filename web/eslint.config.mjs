import nextVitals from "eslint-config-next/core-web-vitals";

export default nextVitals.map(config => config.name === "next/typescript"
  ? { ...config, files: ["**/*.{ts,tsx,mts,cts}"] }
  : config);
