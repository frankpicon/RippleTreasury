export interface PricingTier {
  id: string;
  name: string;
  price: number;
  capacity: number;
}

export interface EventModel {
  id: string;
  name: string;
  description: string;
  venue: string;
  startsAtUtc: string;
  totalCapacity: number;
  version: number;
  pricingTiers: PricingTier[];
}

export interface AvailabilityTier {
  pricingTierId: string;
  name: string;
  price: number;
  capacity: number;
  ticketsSold: number;
  available: number;
  isActive: boolean;
}

export interface Availability {
  eventId: string;
  eventName: string;
  startsAtUtc: string;
  totalCapacity: number;
  ticketsSold: number;
  available: number;
  isActive: boolean;
  pricingTiers: AvailabilityTier[];
}

export interface InventorySummary {
  eventId: string;
  totalCapacity: number;
  ticketsSold: number;
  available: number;
  isActive: boolean;
}

export interface TierSalesSummary {
  pricingTierId: string;
  name: string;
  capacity: number;
  ticketsSold: number;
  available: number;
  grossRevenue: number;
  isActive: boolean;
}

export interface SalesSummary {
  eventId: string;
  eventName: string;
  startsAtUtc: string;
  totalCapacity: number;
  ticketsSold: number;
  available: number;
  grossRevenue: number;
  isActive: boolean;
  pricingTiers: TierSalesSummary[];
}

export interface PurchaseRequest {
  pricingTierId: string;
  customerEmail: string;
  quantity: number;
}

export interface PurchaseResponse {
  purchaseId: string;
  eventId: string;
  pricingTierId: string;
  pricingTierName: string;
  customerEmail: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  purchasedAtUtc: string;
}

export interface ApiProblem {
  title?: string;
  detail?: string;
  traceId?: string;
}

export interface RealtimeUpdate {
  eventId: string;
  changeType: string;
  correlationId: string;
  occurredAtUtc: string;
}
