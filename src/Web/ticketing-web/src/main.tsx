import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import keycloak from "./auth/keycloak";
import "./styles.css";

keycloak.init({ onLoad: "login-required", pkceMethod: "S256", checkLoginIframe: false })
  .then(authenticated => {
    if (!authenticated) {
      return keycloak.login();
    }

    ReactDOM.createRoot(document.getElementById("root")!).render(
      <React.StrictMode><App /></React.StrictMode>
    );
  })
  .catch(error => {
    console.error("Authentication initialization failed", error);
    document.getElementById("root")!.innerHTML =
      '<main class="fatal"><h1>Authentication is unavailable</h1><p>Confirm the local platform is running, then refresh.</p></main>';
  });
