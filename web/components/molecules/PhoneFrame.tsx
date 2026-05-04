import { Mic, Play } from "lucide-react";
import type { ReactNode } from "react";

type ChatMessageProps = {
  type: "user" | "bot";
  text: ReactNode;
  time: string;
};

export function ChatMessage({ type, text, time }: ChatMessageProps) {
  return (
    <div className={`msg ${type}`}>
      <div className="msg-bubble">{text}</div>
      <div className="msg-time">{time}</div>
    </div>
  );
}

export function AudioChatMessage({ time, duration = "0:12" }: { time: string; duration?: string }) {
  return (
    <div className="msg user audio-msg">
      <div className="msg-bubble audio-bubble" aria-label={`Mensagem de áudio de ${duration}`}>
        <span className="audio-play">
          <Play size={13} fill="currentColor" />
        </span>
        <span className="audio-wave" aria-hidden="true">
          <i />
          <i />
          <i />
          <i />
          <i />
          <i />
          <i />
          <i />
        </span>
        <span className="audio-duration">{duration}</span>
        <Mic size={14} className="audio-mic" />
      </div>
      <div className="msg-time">{time}</div>
    </div>
  );
}

export function PhoneFrame({ children, bodyId }: { children?: ReactNode; bodyId?: string }) {
  return (
    <div className="phone-frame">
      <div className="phone-statusbar">
        <span>9:41</span>
        <span>◼︎◼︎◼︎ 🔋</span>
      </div>
      <div className="phone-header">
        <div className="phone-avatar" aria-label="Agente Luma" />
        <div className="phone-contact">
          <div className="phone-contact-name">Luma</div>
          <div className="phone-contact-status">Assistente de ciclo ✦</div>
        </div>
      </div>
      <div className="phone-body" id={bodyId}>
        {children}
      </div>
    </div>
  );
}
