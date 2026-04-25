"use client";

import { useEffect } from "react";
import { refreshWaitlistCount } from "@/components/organisms/WaitlistSection";

const messages = [
  { type: "user", text: "menstruei hoje", time: "10:32" },
  { type: "bot", text: "Registrei o início da sua menstruação hoje ✅\nComo está o fluxo?", time: "10:32" },
  { type: "user", text: "médio", time: "10:33" },
  { type: "bot", text: "Pronto, salvei como fluxo médio. Estou aqui se precisar registrar mais alguma coisa 💛", time: "10:33" },
  { type: "user", text: "tô com uma cólica braba", time: "10:41" },
  { type: "bot", text: "Anotei cólica forte para hoje. Espero que melhore logo 🌿", time: "10:41" },
] as const;

const delay = (ms: number) => new Promise((resolve) => window.setTimeout(resolve, ms));

export function LandingPageBehavior() {
  useEffect(() => {
    const nav = document.getElementById("nav");
    const onScroll = () => nav?.classList.toggle("scrolled", window.scrollY > 40);
    window.addEventListener("scroll", onScroll);
    onScroll();

    const fadeObserver = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) entry.target.classList.add("visible");
        });
      },
      { threshold: 0.12 },
    );
    document.querySelectorAll(".fade-up").forEach((el) => fadeObserver.observe(el));

    const scrollButtons = document.querySelectorAll<HTMLElement>("[data-scroll-target]");
    const onScrollButtonClick = (event: Event) => {
      const target = (event.currentTarget as HTMLElement).dataset.scrollTarget;
      if (!target) return;
      document.getElementById(target)?.scrollIntoView({ behavior: "smooth" });
    };
    scrollButtons.forEach((button) => button.addEventListener("click", onScrollButtonClick));

    const counterEl = document.getElementById("counter-num");
    let countObserver: IntersectionObserver | undefined;
    if (counterEl) {
      countObserver = new IntersectionObserver(
        (entries) => {
          if (entries[0]?.isIntersecting) {
            refreshWaitlistCount();
            countObserver?.disconnect();
          }
        },
        { threshold: 0.5 },
      );
      countObserver.observe(counterEl);
    }

    return () => {
      window.removeEventListener("scroll", onScroll);
      fadeObserver.disconnect();
      countObserver?.disconnect();
      scrollButtons.forEach((button) => button.removeEventListener("click", onScrollButtonClick));
    };
  }, []);

  useEffect(() => {
    const body = document.getElementById("phone-body");
    if (!body) return;
    const chatBody = body;

    let cancelled = false;

    function renderMsg(message: (typeof messages)[number]) {
      if (cancelled) return;
      const el = document.createElement("div");
      el.className = `msg ${message.type}`;
      const bubble = document.createElement("div");
      bubble.className = "msg-bubble";
      bubble.innerHTML = message.text.replace(/\n/g, "<br/>");
      const time = document.createElement("div");
      time.className = "msg-time";
      time.textContent = message.time;
      el.append(bubble, time);
      el.style.opacity = "0";
      el.style.transform = "translateY(8px)";
      chatBody.appendChild(el);
      requestAnimationFrame(() => {
        el.style.transition = "opacity 0.35s, transform 0.35s";
        el.style.opacity = "1";
        el.style.transform = "none";
      });
      chatBody.scrollTop = chatBody.scrollHeight;
    }

    function showTyping() {
      const typing = document.createElement("div");
      typing.className = "chat-typing";
      typing.innerHTML = "<span></span><span></span><span></span>";
      chatBody.appendChild(typing);
      chatBody.scrollTop = chatBody.scrollHeight;
      return typing;
    }

    async function playChat() {
      while (!cancelled) {
        chatBody.innerHTML = "";
        for (let i = 0; i < messages.length; i += 1) {
          await delay(i === 0 ? 900 : 1200);
          if (cancelled) return;
          if (messages[i].type === "bot") {
            const typing = showTyping();
            await delay(900);
            typing.remove();
          }
          renderMsg(messages[i]);
        }
        await delay(3000);
      }
    }

    playChat();

    return () => {
      cancelled = true;
      chatBody.innerHTML = "";
    };
  }, []);

  return null;
}
