
SET statement_timeout = 0;
SET lock_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', 'public', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

-- Table: tenants
DROP TABLE IF EXISTS public.tenants CASCADE;
CREATE TABLE public.tenants (
    "tenant_id" character varying NOT NULL,
    "business_name" character varying NOT NULL,
    "business_type" character varying NOT NULL DEFAULT ''::character varying,
    "db_type" character varying NOT NULL DEFAULT 'PostgreSql'::character varying,
    "connection_string" character varying NOT NULL DEFAULT ''::character varying,
    "time_zone_id" character varying NOT NULL DEFAULT 'UTC'::character varying,
    "phone_country_code" character varying NOT NULL DEFAULT ''::character varying,
    "address" character varying NOT NULL DEFAULT ''::character varying,
    "phone" character varying NOT NULL DEFAULT ''::character varying,
    "working_hours" character varying NOT NULL DEFAULT ''::character varying,
    "services" jsonb NOT NULL DEFAULT '[]'::jsonb,
    "accepted_insurance" jsonb NOT NULL DEFAULT '[]'::jsonb,
    "pricing" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "custom_settings" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "username" character varying,
    "password_hash" character varying,
    "message_provider" character varying NOT NULL DEFAULT 'Meta'::character varying,
    "message_provider_account" character varying NOT NULL DEFAULT ''::character varying,
    "message_provider_token" character varying NOT NULL DEFAULT ''::character varying,
    "message_provider_phone" character varying NOT NULL DEFAULT ''::character varying,
    "is_active" boolean NOT NULL DEFAULT true,
    "created_at" timestamp with time zone NOT NULL DEFAULT now(),
    "updated_at" timestamp with time zone,
    "webhook_url" character varying DEFAULT NULL::character varying,
    "booking_requirements_json" jsonb,
    "service_modality" character varying DEFAULT 'InPerson'::character varying
);

-- Table: tenant_billing
DROP TABLE IF EXISTS public.tenant_billing CASCADE;
CREATE TABLE public.tenant_billing (
    "tenant_id" character varying NOT NULL,
    "plan_type" character varying NOT NULL DEFAULT 'Trial'::character varying,
    "billing_status" character varying NOT NULL DEFAULT 'Active'::character varying,
    "active_until" timestamp with time zone,
    "suspended_at" timestamp with time zone,
    "suspension_reason" character varying,
    "notes" character varying
);


-- Table: chat_sessions
DROP TABLE IF EXISTS public.chat_sessions CASCADE;
CREATE TABLE public.chat_sessions (
    "id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" character varying NOT NULL,
    "user_phone" character varying NOT NULL DEFAULT ''::character varying,
    "history_json" jsonb NOT NULL DEFAULT '[]'::jsonb,
    "needs_human_attention" boolean NOT NULL DEFAULT false,
    "created_at" timestamp with time zone NOT NULL DEFAULT now(),
    "updated_at" timestamp with time zone NOT NULL DEFAULT now()
);

-- Table: audits
DROP TABLE IF EXISTS public.audits CASCADE;
CREATE TABLE public.audits (
    "id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" character varying NOT NULL,
    "session_id" uuid NOT NULL,
    "timestamp" timestamp with time zone NOT NULL DEFAULT now(),
    "event_type" character varying NOT NULL,
    "content" text NOT NULL DEFAULT ''::text,
    "threat_level" character varying,
    "metadata" jsonb NOT NULL DEFAULT '{}'::jsonb
);

-- Data for table audits
ALTER TABLE ONLY public.tenants ADD CONSTRAINT tenants_pkey PRIMARY KEY (tenant_id);
ALTER TABLE ONLY public.tenant_billing ADD CONSTRAINT tenant_billing_pkey PRIMARY KEY (tenant_id);
ALTER TABLE ONLY public.chat_sessions ADD CONSTRAINT chat_sessions_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.audits ADD CONSTRAINT audits_pkey PRIMARY KEY (id);
ALTER TABLE ONLY public.tenant_billing ADD CONSTRAINT tenant_billing_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(tenant_id) ON DELETE CASCADE;

GRANT USAGE ON SCHEMA public TO receptionist_app;
GRANT SELECT,INSERT,DELETE,UPDATE ON ALL TABLES IN SCHEMA public TO receptionist_app;
