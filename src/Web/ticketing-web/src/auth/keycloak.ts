import Keycloak from "keycloak-js";

const keycloak = new Keycloak({
  url: import.meta.env.VITE_KEYCLOAK_URL ?? "http://localhost:8080/identity",
  realm: "ticketing",
  clientId: "ticketing-web"
});

export default keycloak;
