import { ImageResponse } from "next/og";

export function brandIcon(size: number) {
  return new ImageResponse(
    <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center",
      background: "#240b30", color: "#ffffff", fontSize: size * 0.7, fontWeight: 700, borderRadius: size * 0.14 }}>
      L<span style={{ color: "#bd87ff" }}>.</span>
    </div>, { width: size, height: size },
  );
}
