import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "../styles/tokens.css";
import { SplashApp } from "./SplashApp";

createRoot(document.getElementById("splash-root")!).render(
  <StrictMode>
    <SplashApp />
  </StrictMode>,
);
