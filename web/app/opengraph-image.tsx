import { ImageResponse } from "next/og";
export const alt = "Luma IA - ciclo menstrual e gravidez pelo WhatsApp";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";
export default function OpenGraphImage() {
  return new ImageResponse(
    <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column", justifyContent: "center",
      background: "#240b30", color: "#ffffff", padding: "72px", borderBottom: "16px solid #bd87ff" }}>
      <div style={{ display: "flex", fontSize: 112, fontWeight: 700 }}>Luma<span style={{ color: "#bd87ff" }}>.</span></div>
      <div style={{ fontSize: 42, marginTop: 28, maxWidth: 960 }}>Ciclo menstrual e gravidez pelo WhatsApp</div>
      <div style={{ fontSize: 26, marginTop: 36, color: "#e4c7ed" }}>Registros, calendário e lembretes em uma conversa.</div>
      <div style={{ fontSize: 24, marginTop: 48, color: "#e4c7ed" }}>ia-luma.com.br</div>
    </div>, size,
  );
}
