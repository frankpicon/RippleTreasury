FROM node:22-alpine AS build
WORKDIR /app
COPY src/Web/ticketing-web/package*.json ./
RUN npm ci
COPY src/Web/ticketing-web/ .
ARG VITE_API_URL=http://localhost:8080
ARG VITE_KEYCLOAK_URL=http://localhost:8080/identity
ENV VITE_API_URL=$VITE_API_URL
ENV VITE_KEYCLOAK_URL=$VITE_KEYCLOAK_URL
RUN npm run build

FROM nginx:1.27-alpine
COPY src/Web/ticketing-web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
