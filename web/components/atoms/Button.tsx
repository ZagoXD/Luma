import type { AnchorHTMLAttributes, ButtonHTMLAttributes, ReactNode } from "react";

type ButtonLinkProps = AnchorHTMLAttributes<HTMLAnchorElement> & {
  variant: "primary" | "ghost";
  children: ReactNode;
};

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  className?: string;
  children: ReactNode;
};

export function ButtonLink({ variant, children, ...props }: ButtonLinkProps) {
  return (
    <a className={variant === "primary" ? "btn-primary" : "btn-ghost"} {...props}>
      {children}
    </a>
  );
}

export function Button({ className = "nav-cta", children, ...props }: ButtonProps) {
  return (
    <button className={className} {...props}>
      {children}
    </button>
  );
}
