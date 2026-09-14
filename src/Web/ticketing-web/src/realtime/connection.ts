import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection
} from "@microsoft/signalr";
import { apiBaseUrl } from "../api/client";
import keycloak from "../auth/keycloak";

export type RealtimeStatus = "connecting" | "connected" | "reconnecting" | "offline";

export const realtimeEvents = {
  catalogChanged: "CatalogChanged",
  inventoryChanged: "InventoryChanged",
  salesChanged: "SalesChanged"
} as const;

export function createRealtimeConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}/hubs/updates`, {
      accessTokenFactory: async () => {
        await keycloak.updateToken(30);
        return keycloak.token ?? "";
      }
    })
    .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
    .configureLogging(LogLevel.Warning)
    .build();
}
