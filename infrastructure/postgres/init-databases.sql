CREATE USER event_catalog_user WITH PASSWORD 'event_catalog_password';
CREATE DATABASE event_catalog OWNER event_catalog_user;

CREATE USER ticketing_user WITH PASSWORD 'ticketing_password';
CREATE DATABASE ticketing OWNER ticketing_user;

CREATE USER reporting_user WITH PASSWORD 'reporting_password';
CREATE DATABASE reporting OWNER reporting_user;

CREATE USER notifications_user WITH PASSWORD 'notifications_password';
CREATE DATABASE notifications OWNER notifications_user;
