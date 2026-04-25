import type { ReactNode } from "react";
import { SectionTag } from "@/components/atoms/SectionTag";

type SectionHeadingProps = {
  tag: string;
  title: ReactNode;
  lead?: ReactNode;
  centered?: boolean;
  darkTitle?: boolean;
};

export function SectionHeading({ tag, title, lead, centered, darkTitle }: SectionHeadingProps) {
  return (
    <div className="fade-up" style={centered ? { textAlign: "center" } : undefined}>
      <SectionTag>{tag}</SectionTag>
      <h2 className="section-h2" style={darkTitle ? { color: "#fff" } : undefined}>
        {title}
      </h2>
      {lead ? (
        <p className="section-lead" style={centered ? { margin: "0 auto" } : undefined}>
          {lead}
        </p>
      ) : null}
    </div>
  );
}
