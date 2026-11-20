-- =============================================================================
-- 01_schema.sql — RRIDA database schema baseline (public schema only)
--
-- Applied by the deploy pipeline ONLY to an empty database (a brand-new
-- environment such as Pre-Prod), before the EF migrations bundle runs.
-- The EF migrations cannot build the database from nothing: the original
-- schema was created by hand-run SQL scripts, so several tables (e.g.
-- CompanyLicenses) are never created by a migration.
--
-- Generated from the Dev database (PostgreSQL 16) with:
--   pg_dump --schema-only --no-owner --no-privileges --no-comments --schema=public
-- Regenerate with infra/db/bootstrap/export-bootstrap.sh when the baseline
-- should move forward. Migrations newer than 02_reference_data.sql's
-- __EFMigrationsHistory rows are applied afterwards by the pipeline.
-- =============================================================================

--
-- PostgreSQL database dump
--

-- Dumped from database version 16.15

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: public; Type: SCHEMA; Schema: -; Owner: -
--



SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: AIModelConfigs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AIModelConfigs" (
    "Id" integer NOT NULL,
    "PromptName" character varying(150) NOT NULL,
    "ModelName" character varying(100) NOT NULL,
    "Provider" character varying(50) NOT NULL,
    "EndpointUrl" character varying(500) DEFAULT ''::character varying NOT NULL,
    "Role" character varying(20) DEFAULT 'Primary'::character varying NOT NULL,
    "Priority" integer DEFAULT 1 NOT NULL,
    "Strategy" character varying(30),
    "IsActive" boolean DEFAULT true NOT NULL,
    "IsDel" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: AIModelConfigs_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."AIModelConfigs" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."AIModelConfigs_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: AIRequestLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AIRequestLogs" (
    "Id" bigint NOT NULL,
    "PromptName" character varying(150) DEFAULT ''::character varying NOT NULL,
    "PromptVersion" character varying(20) DEFAULT ''::character varying NOT NULL,
    "ModelRequested" character varying(100) DEFAULT ''::character varying NOT NULL,
    "ModelUsed" character varying(100) DEFAULT ''::character varying NOT NULL,
    "FallbackUsed" boolean DEFAULT false NOT NULL,
    "LatencyMs" bigint DEFAULT 0 NOT NULL,
    "PromptTokens" integer DEFAULT 0 NOT NULL,
    "CompletionTokens" integer DEFAULT 0 NOT NULL,
    "EstimatedCost" numeric(10,6) DEFAULT 0 NOT NULL,
    "IsJsonValid" boolean DEFAULT false NOT NULL,
    "AssertionsPassed" integer DEFAULT 0 NOT NULL,
    "AssertionsTotal" integer DEFAULT 0 NOT NULL,
    "Success" boolean DEFAULT false NOT NULL,
    "ErrorMessage" text,
    "BusinessUnitId" bigint,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "ResponsePayload" text,
    "EvalId" character varying(200)
);


--
-- Name: AIRequestLogs_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."AIRequestLogs" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."AIRequestLogs_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ActionEvents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ActionEvents" (
    "Id" integer NOT NULL,
    "DateAddedToAction" timestamp with time zone NOT NULL,
    "ScheduledEventActivity" text NOT NULL,
    "DateImplemented" timestamp with time zone,
    "Feedback" text NOT NULL,
    "Attachment" text NOT NULL,
    "Type" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" integer NOT NULL,
    "IsDel" boolean NOT NULL,
    "ActionHubId" integer NOT NULL
);


--
-- Name: ActionEvents_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ActionEvents" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ActionEvents_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ActionHubs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ActionHubs" (
    "Id" integer NOT NULL,
    "UnitId" bigint NOT NULL,
    "ProfileId" bigint NOT NULL,
    "ActionTitle" text NOT NULL,
    "Description" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" integer NOT NULL,
    "IsDel" boolean NOT NULL,
    "Type" text DEFAULT 0 NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "ReportId" bigint
);


--
-- Name: ActionHubs_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ActionHubs" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ActionHubs_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: AssignUserRights; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AssignUserRights" (
    "AssignRightId" integer NOT NULL,
    "UserGroupId" integer NOT NULL,
    "SystemModuleId" integer NOT NULL,
    "PromptTypeId" integer NOT NULL
);


--
-- Name: AssignUserRights_AssignRightId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."AssignUserRights" ALTER COLUMN "AssignRightId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."AssignUserRights_AssignRightId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: AssignedCompanyLicenseAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AssignedCompanyLicenseAuditLogs" (
    "AuditId" integer NOT NULL,
    "Id" integer NOT NULL,
    "CompanyLicenseId" integer NOT NULL,
    "BusinessUnitId" bigint NOT NULL,
    "AssignedAt" timestamp with time zone NOT NULL,
    "UnassignedAt" timestamp with time zone,
    "IsActive" boolean NOT NULL,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL,
    "LoggedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "VersionNumber" integer DEFAULT 1 NOT NULL,
    "OperationType" character varying(20) NOT NULL,
    "GracePeriod" integer
);


--
-- Name: AssignedCompanyLicenseAuditLogs_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."AssignedCompanyLicenseAuditLogs" ALTER COLUMN "AuditId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."AssignedCompanyLicenseAuditLogs_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: AssignedCompanyLicenses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AssignedCompanyLicenses" (
    "Id" integer NOT NULL,
    "CompanyLicenseId" integer NOT NULL,
    "BusinessUnitId" bigint NOT NULL,
    "AssignedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UnassignedAt" timestamp with time zone,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "IsDel" boolean DEFAULT false NOT NULL,
    "GracePeriod" integer
);


--
-- Name: AssignedCompanyLicenses_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."AssignedCompanyLicenses" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."AssignedCompanyLicenses_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: AuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AuditLogs" (
    "LogId" integer NOT NULL,
    "Timestamp" timestamp with time zone NOT NULL,
    "UserId" integer,
    "Endpoint" text NOT NULL,
    "HttpMethod" text NOT NULL,
    "RequestData" text NOT NULL,
    "ResponseData" text NOT NULL,
    "ActionType" text NOT NULL
);


--
-- Name: AuditLogs_LogId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."AuditLogs" ALTER COLUMN "LogId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."AuditLogs_LogId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: AzureMarketplaceSubscription; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AzureMarketplaceSubscription" (
    "Id" integer NOT NULL,
    "MarketplaceSubscriptionId" uuid NOT NULL,
    "OfferId" character varying(200) NOT NULL,
    "PlanId" character varying(200) NOT NULL,
    "PublisherId" character varying(200),
    "SubscriptionName" character varying(300),
    "Quantity" integer DEFAULT 1 NOT NULL,
    "Status" character varying(50) NOT NULL,
    "PurchaserEmail" character varying(300),
    "PurchaserTenantId" uuid,
    "PurchaserObjectId" character varying(200),
    "BeneficiaryEmail" character varying(300),
    "BeneficiaryTenantId" uuid,
    "BeneficiaryObjectId" character varying(200),
    "TermUnit" character varying(20),
    "TermStartDate" timestamp with time zone,
    "TermEndDate" timestamp with time zone,
    "AutoRenew" boolean DEFAULT false NOT NULL,
    "IsTest" boolean DEFAULT false NOT NULL,
    "IsFreeTrial" boolean DEFAULT false NOT NULL,
    "ProfileId" bigint,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "ActivatedAt" timestamp with time zone,
    "UnsubscribedAt" timestamp with time zone,
    "IsDel" boolean DEFAULT false NOT NULL
);


--
-- Name: AzureMarketplaceSubscription_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."AzureMarketplaceSubscription_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: AzureMarketplaceSubscription_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."AzureMarketplaceSubscription_Id_seq" OWNED BY public."AzureMarketplaceSubscription"."Id";


--
-- Name: AzureMarketplaceWebhookEvent; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AzureMarketplaceWebhookEvent" (
    "Id" integer NOT NULL,
    "OperationId" uuid NOT NULL,
    "ActivityId" uuid,
    "MarketplaceSubscriptionId" uuid NOT NULL,
    "Action" character varying(50) NOT NULL,
    "IncomingStatus" character varying(50),
    "PlanId" character varying(200),
    "Quantity" integer,
    "RawPayload" jsonb DEFAULT '{}'::jsonb NOT NULL,
    "ProcessingStatus" character varying(50) DEFAULT 'Received'::character varying NOT NULL,
    "ErrorMessage" text,
    "VerifiedWithMicrosoft" boolean DEFAULT false NOT NULL,
    "AckSentToMicrosoft" boolean DEFAULT false NOT NULL,
    "ReceivedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "ProcessedAt" timestamp with time zone
);


--
-- Name: AzureMarketplaceWebhookEvent_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."AzureMarketplaceWebhookEvent_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: AzureMarketplaceWebhookEvent_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."AzureMarketplaceWebhookEvent_Id_seq" OWNED BY public."AzureMarketplaceWebhookEvent"."Id";


--
-- Name: BatchJobs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."BatchJobs" (
    "Id" uuid NOT NULL,
    "OpenAIJobId" character varying(255) NOT NULL,
    "Status" character varying(50) NOT NULL,
    "RequestType" character varying(100),
    "SubmittedAt" timestamp without time zone NOT NULL,
    "CompletedAt" timestamp without time zone,
    "InputFileUrl" text,
    "OutputFileUrl" text,
    "ErrorMessage" text,
    "BusinessUnitId" integer NOT NULL,
    "MetadataJson" text
);


--
-- Name: BusinessUnits; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."BusinessUnits" (
    "Id" bigint NOT NULL,
    "BusinessUnitCode" character varying(36) NOT NULL,
    "BusinessUnitShortCode" character varying(8) NOT NULL,
    "Name" character varying(140) NOT NULL,
    "NameInvariant" character varying(140) NOT NULL,
    "RegistrationDate" timestamp with time zone NOT NULL,
    "ProfileId" bigint NOT NULL,
    "ActiveDailyQuestionSetGroupId" bigint NOT NULL,
    "IsDel" boolean,
    "ParentId" integer,
    "TeamSize" integer,
    "ContactEmailAddress" character varying(254),
    "ContactFirstName" character varying(60),
    "ContactLastName" character varying(60)
);


--
-- Name: BusinessUnits_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."BusinessUnits" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."BusinessUnits_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CloveraPersonalSnapshots; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CloveraPersonalSnapshots" (
    "Id" integer NOT NULL,
    "Status" character varying(20) DEFAULT 'Queued'::character varying NOT NULL,
    "ProfileId" bigint NOT NULL,
    "UnitId" bigint NOT NULL,
    "FromDate" timestamp with time zone NOT NULL,
    "ToDate" timestamp with time zone NOT NULL,
    "ModelUsed" character varying(120),
    "Headline" text,
    "SummaryBody" text,
    "ProductivityObservation" text,
    "PillarFraming" text,
    "HighlightedPillarCode" character varying(1),
    "HighlightedPillarLabel" character varying(40),
    "HighlightedSmileyScore" integer,
    "HighlightReason" character varying(40),
    "NudgeTitle" text,
    "NudgeRationale" text,
    "NudgeAction" text,
    "NudgeIntent" character varying(20),
    "DrawingFromJson" text,
    "RawResponseJson" text,
    "PromptTokens" integer DEFAULT 0 NOT NULL,
    "CompletionTokens" integer DEFAULT 0 NOT NULL,
    "TotalTokens" integer DEFAULT 0 NOT NULL,
    "GenerationSeconds" double precision DEFAULT 0 NOT NULL,
    "ErrorMessage" text,
    "IsDel" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT (now() AT TIME ZONE 'UTC'::text) NOT NULL,
    "CompletedAt" timestamp with time zone,
    "CreatedBy" integer
);


--
-- Name: CloveraPersonalSnapshots_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CloveraPersonalSnapshots" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."CloveraPersonalSnapshots_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Profiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Profiles" (
    "Id" bigint NOT NULL,
    "ProfileShortCode" character varying(8) NOT NULL,
    "DomainName" character varying(253) NOT NULL,
    "ProfileName" character varying(140) NOT NULL,
    "RegistrationDate" timestamp with time zone NOT NULL,
    "IsPartner" boolean DEFAULT false NOT NULL,
    "ReferredBy" integer,
    "PartnerId" integer,
    "IsDel" boolean DEFAULT false NOT NULL,
    "ContactEmailAddress" character varying(254),
    "ContactFirstName" character varying(60),
    "ContactLastName" character varying(60),
    "ParentId" bigint
);


--
-- Name: Companies_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Profiles" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."Companies_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CompanyLicenseAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CompanyLicenseAuditLogs" (
    "AuditId" integer NOT NULL,
    "Id" integer NOT NULL,
    "ProfileId" bigint NOT NULL,
    "LicenseKey" character varying(64) NOT NULL,
    "LicenseType" character varying(20) NOT NULL,
    "CompanySubscriptionId" integer,
    "PlanId" integer,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL,
    "LoggedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "VersionNumber" integer DEFAULT 1 NOT NULL,
    "OperationType" character varying(20) NOT NULL,
    "ExpiryDate" timestamp with time zone,
    "Notes" character varying(1000)
);


--
-- Name: CompanyLicenseAuditLogs_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CompanyLicenseAuditLogs" ALTER COLUMN "AuditId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."CompanyLicenseAuditLogs_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CompanyLicenses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CompanyLicenses" (
    "Id" integer NOT NULL,
    "ProfileId" bigint NOT NULL,
    "LicenseKey" character varying(64) NOT NULL,
    "LicenseType" character varying(20) NOT NULL,
    "CompanySubscriptionId" integer,
    "PlanId" integer,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "IsDel" boolean DEFAULT false NOT NULL,
    "ExpiryDate" timestamp with time zone,
    "Notes" character varying(1000),
    "ExpiryNotifiedAt" timestamp with time zone,
    "ReactivationNotifiedAt" timestamp with time zone
);


--
-- Name: CompanyLicenses_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CompanyLicenses" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."CompanyLicenses_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CompanyLinks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CompanyLinks" (
    "CompanyLinkId" integer NOT NULL,
    "ProfileId" integer NOT NULL,
    "ParentId" integer NOT NULL,
    "UserId" integer NOT NULL,
    "UnitId" integer NOT NULL,
    "ExpiryDate" text DEFAULT ''::text NOT NULL,
    "CreatedBy" text,
    "BussinessUnitId" integer
);


--
-- Name: CompanyLinks_CompanyLinkId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CompanyLinks" ALTER COLUMN "CompanyLinkId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."CompanyLinks_CompanyLinkId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CompanySubscriptionAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CompanySubscriptionAuditLogs" (
    "AuditId" integer NOT NULL,
    "Id" integer NOT NULL,
    "ProfileId" bigint NOT NULL,
    "PlanId" integer,
    "BillingCycle" integer NOT NULL,
    "Status" integer NOT NULL,
    "StripeCustomerId" character varying(100),
    "StripeSubscriptionId" character varying(100),
    "TeamCount" integer NOT NULL,
    "MaxTeamsAllowed" integer NOT NULL,
    "MaxPeopleAllowed" integer NOT NULL,
    "TrialStartDate" timestamp with time zone,
    "TrialEndDate" timestamp with time zone,
    "CurrentPeriodStart" timestamp with time zone NOT NULL,
    "CurrentPeriodEnd" timestamp with time zone NOT NULL,
    "IsTrialUsed" boolean NOT NULL,
    "CanceledAt" timestamp with time zone,
    "CancelAtPeriodEnd" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "PromoCodeId" integer,
    "LifetimeDiscountPercent" numeric(5,2),
    "IsDel" boolean NOT NULL,
    "LoggedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "VersionNumber" integer DEFAULT 1 NOT NULL,
    "OperationType" character varying(20) DEFAULT ''::character varying NOT NULL,
    "Discount" numeric(5,2),
    "GracePeriod" integer,
    "LastWarningDaysSent" integer
);


--
-- Name: CompanySubscriptionAuditLogs_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CompanySubscriptionAuditLogs" ALTER COLUMN "AuditId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."CompanySubscriptionAuditLogs_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CompanySubscriptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CompanySubscriptions" (
    "Id" integer NOT NULL,
    "ProfileId" bigint NOT NULL,
    "PlanId" integer,
    "BillingCycle" integer NOT NULL,
    "Status" integer NOT NULL,
    "StripeCustomerId" character varying(100),
    "StripeSubscriptionId" character varying(100),
    "MaxTeamsAllowed" integer DEFAULT 0 NOT NULL,
    "MaxPeopleAllowed" integer DEFAULT 0 NOT NULL,
    "TrialStartDate" timestamp with time zone,
    "TrialEndDate" timestamp with time zone,
    "IsTrialUsed" boolean DEFAULT false NOT NULL,
    "CurrentPeriodStart" timestamp with time zone DEFAULT now() NOT NULL,
    "CurrentPeriodEnd" timestamp with time zone DEFAULT now() NOT NULL,
    "CanceledAt" timestamp with time zone,
    "CancelAtPeriodEnd" boolean DEFAULT false NOT NULL,
    "IsDel" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "LifetimeDiscountPercent" numeric(5,2),
    "Discount" numeric(5,2),
    "GracePeriod" integer,
    "LastWarningDaysSent" integer,
    "StripeProductId" character varying(100),
    "TeamCount" integer DEFAULT 1 NOT NULL,
    "PromoCodeId" integer
);


--
-- Name: CompanySubscriptions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CompanySubscriptions" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."CompanySubscriptions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CompanyTeams; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CompanyTeams" (
    "TeamId" integer NOT NULL,
    "TeamName" text NOT NULL,
    "CompanyId" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" integer NOT NULL,
    "ModifiedBy" integer,
    "OrderBy" integer NOT NULL,
    "IsDeleted" boolean NOT NULL,
    "ParentId" integer,
    "UnitId" integer DEFAULT 0 NOT NULL
);


--
-- Name: CompanyTeams_TeamId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CompanyTeams" ALTER COLUMN "TeamId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."CompanyTeams_TeamId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ComplimentaryLicenseGrantAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ComplimentaryLicenseGrantAuditLogs" (
    "AuditId" integer NOT NULL,
    "Id" integer NOT NULL,
    "CompanyLicenseId" integer NOT NULL,
    "PrimaryBusinessUnitId" bigint NOT NULL,
    "ComplimentaryBusinessUnitId" bigint NOT NULL,
    "ManagerUserId" integer NOT NULL,
    "GrantedAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    "IsActive" boolean NOT NULL,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL,
    "LoggedAt" timestamp with time zone NOT NULL,
    "VersionNumber" integer NOT NULL,
    "OperationType" character varying(20) NOT NULL
);


--
-- Name: ComplimentaryLicenseGrantAuditLogs_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ComplimentaryLicenseGrantAuditLogs" ALTER COLUMN "AuditId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."ComplimentaryLicenseGrantAuditLogs_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ComplimentaryLicenseGrants; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ComplimentaryLicenseGrants" (
    "Id" integer NOT NULL,
    "CompanyLicenseId" integer NOT NULL,
    "PrimaryBusinessUnitId" bigint NOT NULL,
    "ComplimentaryBusinessUnitId" bigint NOT NULL,
    "ManagerUserId" integer NOT NULL,
    "GrantedAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    "IsActive" boolean NOT NULL,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL
);


--
-- Name: ComplimentaryLicenseGrants_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ComplimentaryLicenseGrants" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."ComplimentaryLicenseGrants_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ContentCultures; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ContentCultures" (
    "Id" bigint NOT NULL,
    "CultureName" character varying(5) NOT NULL
);


--
-- Name: ContentCultures_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ContentCultures" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."ContentCultures_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CurrentRelationshipStatuses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CurrentRelationshipStatuses" (
    "Id" integer NOT NULL,
    "Name" character varying(50)
);


--
-- Name: CustomQuestionAudits; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CustomQuestionAudits" (
    "AuditId" integer NOT NULL,
    "CustomQuestionId" bigint NOT NULL,
    "Text" character varying(1000) NOT NULL,
    "ContentCultureId" bigint NOT NULL,
    "ScheduledAt" timestamp with time zone,
    "Status" integer NOT NULL,
    "ChangeSummary" character varying(1000),
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL,
    "LoggedAt" timestamp with time zone NOT NULL,
    "VersionNumber" integer NOT NULL,
    "OperationType" character varying(20) NOT NULL,
    "ProfileId" bigint DEFAULT 0 NOT NULL,
    "UnitId" bigint DEFAULT 0 NOT NULL
);


--
-- Name: CustomQuestionAudits_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CustomQuestionAudits" ALTER COLUMN "AuditId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."CustomQuestionAudits_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CustomQuestionDailyFeedback; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CustomQuestionDailyFeedback" (
    "Id" bigint NOT NULL,
    "CustomQuestionId" bigint NOT NULL,
    "FreeText" character varying(300),
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UnitId" bigint DEFAULT 0 NOT NULL
);


--
-- Name: CustomQuestionDailyFeedbackAudits; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CustomQuestionDailyFeedbackAudits" (
    "AuditId" integer NOT NULL,
    "CustomQuestionDailyFeedbackId" bigint NOT NULL,
    "CustomQuestionId" bigint NOT NULL,
    "FreeText" character varying(300),
    "ChangeSummary" character varying(1000),
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "LoggedAt" timestamp with time zone NOT NULL,
    "VersionNumber" integer NOT NULL,
    "OperationType" character varying(20) NOT NULL,
    "UnitId" bigint DEFAULT 0 NOT NULL
);


--
-- Name: CustomQuestionDailyFeedbackAudits_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CustomQuestionDailyFeedbackAudits" ALTER COLUMN "AuditId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."CustomQuestionDailyFeedbackAudits_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CustomQuestionDailyFeedback_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CustomQuestionDailyFeedback" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."CustomQuestionDailyFeedback_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CustomQuestions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CustomQuestions" (
    "Id" bigint NOT NULL,
    "Text" character varying(1000) NOT NULL,
    "ContentCultureId" bigint NOT NULL,
    "ScheduledAt" timestamp with time zone,
    "Status" integer NOT NULL,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL,
    "ProfileId" bigint DEFAULT 0 NOT NULL,
    "UnitId" bigint DEFAULT 0 NOT NULL
);


--
-- Name: CustomQuestions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CustomQuestions" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."CustomQuestions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyFeedback; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyFeedback" (
    "Id" bigint NOT NULL,
    "SubmissionDateTime" timestamp with time zone NOT NULL,
    "BusinessUnitId" bigint NOT NULL,
    "SentimentType" integer NOT NULL,
    "DailyQuestionId" bigint NOT NULL,
    "DailyReasonId" bigint,
    "FreeText" character varying(896)
);


--
-- Name: DailyFeedbackAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyFeedbackAuditLogs" (
    "AuditId" integer NOT NULL,
    "Id" bigint NOT NULL,
    "SubmissionDateTime" timestamp with time zone NOT NULL,
    "BusinessUnitId" bigint NOT NULL,
    "SentimentType" integer NOT NULL,
    "DailyQuestionId" bigint NOT NULL,
    "DailyReasonId" bigint NOT NULL,
    "FreeText" character varying(896),
    "LoggedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "VersionNumber" integer DEFAULT 1 NOT NULL,
    "OperationType" character varying(20) NOT NULL,
    "RecordCountSnapshot" integer
);


--
-- Name: DailyFeedbackAuditLogs_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyFeedbackAuditLogs" ALTER COLUMN "AuditId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."DailyFeedbackAuditLogs_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyFeedbackReasons; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyFeedbackReasons" (
    "Id" bigint NOT NULL,
    "DailyFeedbackId" bigint NOT NULL,
    "DailyReasonId" bigint NOT NULL
);


--
-- Name: DailyFeedbackReasons_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyFeedbackReasons" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DailyFeedbackReasons_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyFeedback_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyFeedback" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DailyFeedback_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyMicroActions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyMicroActions" (
    "Id" bigint NOT NULL,
    "Text" character varying(1000) NOT NULL,
    "ContentCultureId" bigint NOT NULL,
    "ReasonId" bigint NOT NULL
);


--
-- Name: DailyMicroActions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyMicroActions" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DailyMicroActions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyQuestionSetGroups; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyQuestionSetGroups" (
    "Id" bigint NOT NULL,
    "DailyQuestionSetType" integer NOT NULL
);


--
-- Name: DailyQuestionSetGroups_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyQuestionSetGroups" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DailyQuestionSetGroups_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyQuestionSets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyQuestionSets" (
    "Id" bigint NOT NULL,
    "VersionNumber" bigint NOT NULL,
    "Name" character varying(240) NOT NULL,
    "ContentCultureId" bigint NOT NULL,
    "DailyQuestionSetGroupId" bigint NOT NULL
);


--
-- Name: DailyQuestionSets_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyQuestionSets" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DailyQuestionSets_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyQuestionSpecifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyQuestionSpecifications" (
    "Id" bigint NOT NULL,
    "DailyQuestionSetGroupId" bigint NOT NULL,
    "DailyQuestionSetId" bigint NOT NULL,
    "CloverCategory" integer NOT NULL,
    "Ordinal" integer NOT NULL,
    "ContentCultureId" bigint NOT NULL,
    "QuestionId" bigint NOT NULL
);


--
-- Name: DailyQuestionSpecifications_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyQuestionSpecifications" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DailyQuestionSpecifications_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyQuestions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyQuestions" (
    "Id" bigint NOT NULL,
    "Text" character varying(1000) NOT NULL,
    "ContentCultureId" bigint NOT NULL
);


--
-- Name: DailyQuestions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyQuestions" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DailyQuestions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyReasons; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyReasons" (
    "Id" bigint NOT NULL,
    "DailyReasonSentimentType" integer NOT NULL,
    "Text" character varying(1000) NOT NULL,
    "RequiresFreeTextFeedback" boolean NOT NULL,
    "SpecificationId" bigint NOT NULL,
    "ContentCultureId" bigint NOT NULL
);


--
-- Name: DailyReasons_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyReasons" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DailyReasons_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DailyReflections; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DailyReflections" (
    "Id" bigint NOT NULL,
    "BusinessUnitId" bigint NOT NULL,
    "ReflectionLocalDate" date NOT NULL,
    "AchievementsLines" text[],
    "NotDoneLines" text[],
    "BlockersLines" text[],
    "TomorrowReminderText" character varying(500),
    "TomorrowReminderLocalTime" character varying(20),
    "ClientSubmittedAtUtc" timestamp with time zone,
    "IsDel" boolean DEFAULT false NOT NULL,
    "PersonalBusinessUnitId" integer DEFAULT 0 NOT NULL
);


--
-- Name: DailyReflections_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DailyReflections" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DailyReflections_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: DocumentEmbeddingAuditLogs_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."DocumentEmbeddingAuditLogs_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: DocumentEmbeddingAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DocumentEmbeddingAuditLogs" (
    "Id" integer DEFAULT nextval('public."DocumentEmbeddingAuditLogs_Id_seq"'::regclass) NOT NULL,
    "Action" character varying(50) NOT NULL,
    "ProcessId" integer,
    "FileName" character varying(500),
    "EmbeddingType" character varying(200),
    "SourceType" character varying(100),
    "TotalChunks" integer,
    "Details" character varying(1000),
    "CreatedAt" timestamp without time zone DEFAULT (now() AT TIME ZONE 'utc'::text) NOT NULL
);


--
-- Name: DocumentEmbeddings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DocumentEmbeddings" (
    "Id" integer NOT NULL,
    "Title" character varying(500) NOT NULL,
    "Content" text NOT NULL,
    "Embedding" public.vector(1536) NOT NULL,
    "SourceType" character varying(100) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "FileName" character varying(500),
    "ChunkIndex" integer,
    "EmbeddingType" character varying(200),
    "ProcessId" integer DEFAULT 0 NOT NULL
);


--
-- Name: DocumentEmbeddings_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DocumentEmbeddings" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DocumentEmbeddings_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: EngagementInsights; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."EngagementInsights" (
    "Id" integer NOT NULL,
    "BusinessUnitId" integer NOT NULL,
    "GeneratedAt" timestamp without time zone NOT NULL,
    "PeriodStart" date NOT NULL,
    "PeriodEnd" date NOT NULL,
    "OverallScore" numeric(5,2),
    "CategoryScoresJson" text,
    "TopThemesJson" text,
    "RecommendationsJson" text,
    "StatisticalSummaryJson" jsonb,
    "CreatedBy" character varying(255),
    "BatchJobsId" uuid NOT NULL,
    "TopActionsJson" text
);


--
-- Name: EranCoachConversations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."EranCoachConversations" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Title" character varying(200),
    "IsPinned" boolean DEFAULT false NOT NULL,
    "ProfileId" bigint,
    "BusinessUnitId" bigint,
    "UserGroupId" integer,
    "LastMessageAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT (now() AT TIME ZONE 'UTC'::text) NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT (now() AT TIME ZONE 'UTC'::text) NOT NULL,
    "IsDel" boolean DEFAULT false NOT NULL
);


--
-- Name: EranCoachConversations_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."EranCoachConversations" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."EranCoachConversations_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: EranCoachManagerMemories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."EranCoachManagerMemories" (
    "Id" integer NOT NULL,
    "ManagerUserId" integer NOT NULL,
    "SummaryText" text,
    "TurnCount" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL
);


--
-- Name: EranCoachManagerMemories_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."EranCoachManagerMemories" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."EranCoachManagerMemories_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: EranCoachMessages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."EranCoachMessages" (
    "Id" bigint NOT NULL,
    "ConversationId" integer NOT NULL,
    "Role" character varying(20) NOT NULL,
    "Content" text NOT NULL,
    "PromptTokens" integer,
    "CompletionTokens" integer,
    "ModelUsed" character varying(120),
    "CreatedAt" timestamp with time zone DEFAULT (now() AT TIME ZONE 'UTC'::text) NOT NULL,
    "IsDel" boolean DEFAULT false NOT NULL
);


--
-- Name: EranCoachMessages_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."EranCoachMessages" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."EranCoachMessages_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: FeedBack; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."FeedBack" (
    "Id" bigint NOT NULL,
    "BusinessUnitId" bigint NOT NULL,
    "FeedbackText" text NOT NULL,
    "UserSubmittedAtUtc" timestamp without time zone NOT NULL
);


--
-- Name: FeedBack_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."FeedBack" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."FeedBack_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: FeedbackAnalysisResults; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."FeedbackAnalysisResults" (
    "Id" integer NOT NULL,
    "AnalysisDate" timestamp with time zone NOT NULL,
    "SubmissionDate" timestamp with time zone NOT NULL,
    "CategoryName" text NOT NULL,
    "SadPercentage" double precision NOT NULL,
    "HappyPercentage" double precision NOT NULL,
    "IndifferentPercentage" double precision NOT NULL,
    "AngryPercentage" double precision NOT NULL,
    "BusinessUnitId" bigint NOT NULL
);


--
-- Name: FeedbackAnalysisResults_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."FeedbackAnalysisResults" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."FeedbackAnalysisResults_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Industries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Industries" (
    "Id" integer NOT NULL,
    "Name" character varying(50)
);


--
-- Name: InsightPublishLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."InsightPublishLogs" (
    "Id" bigint NOT NULL,
    "ReportId" bigint NOT NULL,
    "RunId" integer NOT NULL,
    "TopThemes" text DEFAULT '[]'::text NOT NULL,
    "TopActions" text DEFAULT '[]'::text NOT NULL,
    "TopRecommendations" text DEFAULT '[]'::text NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UserId" integer,
    "DateFrom" timestamp with time zone DEFAULT now() NOT NULL,
    "DateTo" timestamp with time zone DEFAULT now() NOT NULL,
    "OperationType" character varying(10) DEFAULT 'Insert'::character varying NOT NULL
);


--
-- Name: InsightPublishLogs_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."InsightPublishLogs_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: InsightPublishLogs_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."InsightPublishLogs_Id_seq" OWNED BY public."InsightPublishLogs"."Id";


--
-- Name: LLMLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."LLMLogs" (
    "LogId" integer NOT NULL,
    "Timestamp" timestamp without time zone DEFAULT (now() AT TIME ZONE 'UTC'::text) NOT NULL,
    "UnitId" integer DEFAULT 0 NOT NULL,
    "FromDate" timestamp without time zone,
    "ToDate" timestamp without time zone,
    "PromptTokens" text,
    "UserId" integer DEFAULT 0 NOT NULL,
    "Endpoint" text,
    "HttpMethod" text,
    "TokenCount" integer NOT NULL,
    "RequestData" text,
    "ResponseData" text,
    "Status" text
);


--
-- Name: LLMLogs_LogId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."LLMLogs" ALTER COLUMN "LogId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."LLMLogs_LogId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: LLMPrompts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."LLMPrompts" (
    "Id" integer NOT NULL,
    "InitialPrompt" text NOT NULL
);


--
-- Name: LLMReports; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."LLMReports" (
    "Id" bigint NOT NULL,
    "FromDate" timestamp with time zone NOT NULL,
    "ToDate" timestamp with time zone NOT NULL,
    "UnitId" bigint NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "WorksheetFile" text
);


--
-- Name: LLMReports_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."LLMReports" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."LLMReports_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ModelConfiguration; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ModelConfiguration" (
    "Id" integer NOT NULL,
    "ModelName" character varying(255),
    "FriendlyName" character varying(255),
    "ApiKey" text,
    "Endpoint" text,
    "ApiVersion" character varying(100),
    "MaxTokens" integer DEFAULT 4096 NOT NULL,
    "Temperature" double precision DEFAULT 0.3 NOT NULL,
    "IsDefault" boolean DEFAULT false NOT NULL,
    "ModelConfigTypeId" integer NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    "CreatedBy" character varying(255),
    "ModifiedAt" timestamp without time zone,
    "ModifiedBy" character varying(255),
    "IsDel" boolean DEFAULT false NOT NULL,
    "Provider" text DEFAULT ''::text NOT NULL,
    "Source" text DEFAULT ''::text NOT NULL,
    "InputCostPer1k" numeric DEFAULT 0 NOT NULL,
    "OutputCostPer1k" numeric DEFAULT 0 NOT NULL,
    "TopP" double precision DEFAULT 1.0 NOT NULL,
    "IsSelected" boolean DEFAULT false NOT NULL
);


--
-- Name: ModelConfigurationTypes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ModelConfigurationTypes" (
    "Id" integer NOT NULL,
    "Name" character varying(100) NOT NULL
);


--
-- Name: ModelConfiguration_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ModelConfiguration" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ModelConfiguration_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Notifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Notifications" (
    "NotificationId" integer NOT NULL,
    "UnitId" integer NOT NULL,
    "ProfileId" integer NOT NULL,
    "UserId" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "Message" text NOT NULL,
    "Type" integer,
    "BussinessUnitId" integer DEFAULT 0 NOT NULL
);


--
-- Name: Notifications_NotificationId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Notifications" ALTER COLUMN "NotificationId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Notifications_NotificationId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: NumberOfEmployees; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."NumberOfEmployees" (
    "Id" integer NOT NULL,
    "Name" character varying(50)
);


--
-- Name: Opportunities; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Opportunities" (
    "Id" integer NOT NULL,
    "CompanyName" character varying(255) NOT NULL,
    "CompanyWebsite" character varying(255),
    "PrimaryContactName" character varying(255) NOT NULL,
    "PrimaryContactTitle" character varying(255) NOT NULL,
    "PrimaryContactEmail" character varying(255),
    "PrimaryContactPhone" character varying(50),
    "Industryid" integer NOT NULL,
    "NumberOfEmployeesid" integer NOT NULL,
    "CompanyLocation" character varying(255) NOT NULL,
    stageid integer NOT NULL,
    "ExpectedCloseDate" timestamp without time zone NOT NULL,
    "EstimatedDealValue" numeric(18,2),
    "EstimatedRevenue" numeric(18,2),
    "EstimatedTeams" integer,
    "BusinessPainPoints" text NOT NULL,
    "CurrentRelationshipStatusid" integer NOT NULL,
    "AdditionalNotes" text,
    "AcceptTerms1" boolean DEFAULT false,
    "AcceptTerms2" boolean DEFAULT false,
    "ProfileId" integer,
    "IsDel" boolean DEFAULT false,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: Opportunities_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Opportunities" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Opportunities_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: OpportunityTypes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."OpportunityTypes" (
    "Id" integer NOT NULL,
    "Name" character varying(100)
);


--
-- Name: PartnerCustomerAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PartnerCustomerAuditLogs" (
    "Id" bigint NOT NULL,
    "Timestamp" timestamp with time zone DEFAULT (now() AT TIME ZONE 'utc'::text) NOT NULL,
    "ActorUserId" integer NOT NULL,
    "CustomerId" bigint NOT NULL,
    "OldPartnerId" bigint,
    "NewPartnerId" bigint,
    "ActionType" character varying(20) NOT NULL
);


--
-- Name: PartnerCustomerAuditLogs_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."PartnerCustomerAuditLogs" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."PartnerCustomerAuditLogs_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: PaymentAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PaymentAuditLogs" (
    "AuditId" integer NOT NULL,
    "Id" integer NOT NULL,
    "ProfileId" bigint NOT NULL,
    "CompanySubscriptionId" integer NOT NULL,
    "StripePaymentIntentId" character varying(100),
    "StripeInvoiceId" character varying(100),
    "Amount" numeric(10,2) NOT NULL,
    "Currency" character varying(3) DEFAULT 'usd'::character varying NOT NULL,
    "PaymentMethod" integer NOT NULL,
    "Status" integer NOT NULL,
    "InvoiceDueDate" timestamp with time zone,
    "VerifiedByUserId" integer,
    "VerifiedAt" timestamp with time zone,
    "Description" character varying(500),
    "FailureReason" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "LoggedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "VersionNumber" integer DEFAULT 1 NOT NULL,
    "OperationType" character varying(20) DEFAULT ''::character varying NOT NULL,
    "CardLast4" character varying(4),
    "CardBrand" character varying(20),
    "CardholderName" character varying(255),
    "CardExpMonth" integer,
    "CardExpYear" integer,
    "StripePaymentMethodId" character varying(100),
    "CardFunding" character varying(20),
    "CardCountry" character varying(2),
    "DiscountCode" character varying(100),
    "AmountSubtotal" numeric(10,2),
    "DiscountAmount" numeric(10,2)
);


--
-- Name: PaymentAuditLogs_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."PaymentAuditLogs" ALTER COLUMN "AuditId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."PaymentAuditLogs_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Payments" (
    "Id" integer NOT NULL,
    "ProfileId" bigint NOT NULL,
    "CompanySubscriptionId" integer NOT NULL,
    "StripePaymentIntentId" character varying(100),
    "StripeInvoiceId" character varying(100),
    "Amount" numeric(10,2) DEFAULT 0 NOT NULL,
    "Currency" character varying(3) DEFAULT 'usd'::character varying NOT NULL,
    "PaymentMethod" integer NOT NULL,
    "Status" integer NOT NULL,
    "InvoiceDueDate" timestamp with time zone,
    "VerifiedByUserId" integer,
    "VerifiedAt" timestamp with time zone,
    "Description" character varying(500),
    "FailureReason" character varying(500),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "CardLast4" character varying(4),
    "CardBrand" character varying(20),
    "CardholderName" character varying(255),
    "CardExpMonth" integer,
    "CardExpYear" integer,
    "StripePaymentMethodId" character varying(100),
    "CardFunding" character varying(20),
    "CardCountry" character varying(2),
    "DiscountCode" character varying(100),
    "AmountSubtotal" numeric(10,2),
    "DiscountAmount" numeric(10,2)
);


--
-- Name: Payments_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Payments" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Payments_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Plans; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Plans" (
    "Id" integer NOT NULL,
    "Name" character varying(50) NOT NULL,
    "Slug" character varying(30) NOT NULL,
    "MaxTeams" integer DEFAULT 0 NOT NULL,
    "MaxPeoplePerTeam" integer DEFAULT 20 NOT NULL,
    "MaxPeople" integer DEFAULT 0 NOT NULL,
    "MonthlyPrice" numeric(10,2) DEFAULT 0 NOT NULL,
    "AnnualPrice" numeric(10,2) DEFAULT 0 NOT NULL,
    "PricePerTeamMonthly" numeric(10,2) DEFAULT 0 NOT NULL,
    "SavingsPercent" integer DEFAULT 0 NOT NULL,
    "IsCustom" boolean DEFAULT false NOT NULL,
    "IsPopular" boolean DEFAULT false NOT NULL,
    "RequiresCreditCard" boolean DEFAULT false NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "Features" jsonb,
    "TrialDays" integer DEFAULT 14 NOT NULL,
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "StripePriceIdMonthly" character varying(100),
    "StripePriceIdAnnual" character varying(100),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: Plans_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Plans" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Plans_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: PortfolioManagerAssignments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PortfolioManagerAssignments" (
    "Id" integer NOT NULL,
    "PortfolioManagerUserId" integer NOT NULL,
    "ManagerUserId" integer NOT NULL,
    "ProfileId" bigint NOT NULL,
    "IsUnAssigned" boolean DEFAULT false NOT NULL,
    "CreatedBy" integer NOT NULL,
    "CreatedDate" timestamp with time zone DEFAULT now() NOT NULL,
    "UnAssignedDate" timestamp with time zone,
    CONSTRAINT "CK_PortfolioManagerAssignments_NotSelf" CHECK (("PortfolioManagerUserId" <> "ManagerUserId"))
);


--
-- Name: PortfolioManagerAssignments_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."PortfolioManagerAssignments" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."PortfolioManagerAssignments_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: PromoCodeAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PromoCodeAuditLogs" (
    "AuditId" integer NOT NULL,
    "Id" integer NOT NULL,
    "Code" character varying(50) NOT NULL,
    "DiscountPercent" numeric(5,2) NOT NULL,
    "ProfileId" bigint,
    "ApplicableBillingCycle" integer,
    "MaxUsageCount" integer,
    "CurrentUsageCount" integer NOT NULL,
    "ExpiryDate" timestamp with time zone NOT NULL,
    "IsLifetime" boolean NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedByUserId" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL,
    "LoggedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "VersionNumber" integer DEFAULT 1 NOT NULL,
    "OperationType" character varying(20) DEFAULT ''::character varying NOT NULL
);


--
-- Name: PromoCodeAuditLogs_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."PromoCodeAuditLogs" ALTER COLUMN "AuditId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."PromoCodeAuditLogs_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: PromoCodeUsageAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PromoCodeUsageAuditLogs" (
    "AuditId" integer NOT NULL,
    "Id" integer NOT NULL,
    "PromoCodeId" integer NOT NULL,
    "ProfileId" bigint NOT NULL,
    "CompanySubscriptionId" integer,
    "UsedAt" timestamp with time zone NOT NULL,
    "LoggedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "VersionNumber" integer DEFAULT 1 NOT NULL,
    "OperationType" character varying(20) DEFAULT ''::character varying NOT NULL
);


--
-- Name: PromoCodeUsageAuditLogs_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."PromoCodeUsageAuditLogs" ALTER COLUMN "AuditId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."PromoCodeUsageAuditLogs_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: PromoCodeUsages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PromoCodeUsages" (
    "Id" integer NOT NULL,
    "PromoCodeId" integer NOT NULL,
    "ProfileId" integer NOT NULL,
    "CompanySubscriptionId" integer,
    "UsedAt" timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: PromoCodeUsages_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."PromoCodeUsages_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: PromoCodeUsages_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."PromoCodeUsages_Id_seq" OWNED BY public."PromoCodeUsages"."Id";


--
-- Name: PromoCodes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PromoCodes" (
    "Id" integer NOT NULL,
    "Code" character varying(50) NOT NULL,
    "DiscountPercent" numeric(5,2) NOT NULL,
    "ProfileId" integer,
    "ApplicableBillingCycle" integer,
    "MaxUsageCount" integer,
    "CurrentUsageCount" integer DEFAULT 0 NOT NULL,
    "ExpiryDate" timestamp without time zone NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedByUserId" integer NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "IsDel" boolean DEFAULT false NOT NULL,
    "IsLifetime" boolean DEFAULT false NOT NULL
);


--
-- Name: PromoCodes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."PromoCodes_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: PromoCodes_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."PromoCodes_Id_seq" OWNED BY public."PromoCodes"."Id";


--
-- Name: PromptConfiguration; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PromptConfiguration" (
    "Id" integer NOT NULL,
    "Text" text,
    "IsDefault" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    "CreatedBy" character varying(255),
    "ModifiedAt" timestamp without time zone,
    "ModifiedBy" character varying(255),
    "IsDel" boolean DEFAULT false NOT NULL
);


--
-- Name: PromptConfiguration_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."PromptConfiguration" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."PromptConfiguration_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: PromptTemplates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PromptTemplates" (
    "Id" integer NOT NULL,
    "Name" character varying(150) NOT NULL,
    "Version" character varying(20) NOT NULL,
    "Description" character varying(500) DEFAULT ''::character varying NOT NULL,
    "SystemPrompt" text DEFAULT ''::text NOT NULL,
    "Template" text DEFAULT ''::text NOT NULL,
    "IsActive" boolean DEFAULT false NOT NULL,
    "IsDel" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedBy" character varying(256),
    "UpdatedAt" timestamp with time zone,
    "MetadataJson" text
);


--
-- Name: PromptTemplates_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."PromptTemplates" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."PromptTemplates_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: PromptTypes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PromptTypes" (
    "PromptTypeId" integer NOT NULL,
    "PromptTypeTitle" text NOT NULL,
    "UnitId" integer NOT NULL,
    "OrganizationId" integer NOT NULL
);


--
-- Name: ResetPasswordLinks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ResetPasswordLinks" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "ResetLink" text NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    "CreatedBy" timestamp without time zone NOT NULL,
    "ExpiryDateTime" timestamp without time zone NOT NULL,
    "IsLinkUsed" boolean DEFAULT false,
    "IsDel" boolean DEFAULT false
);


--
-- Name: ResetPasswordLinks_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ResetPasswordLinks" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."ResetPasswordLinks_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: SeedingHistories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."SeedingHistories" (
    "Id" bigint NOT NULL,
    "MajorVersionNumber" integer NOT NULL,
    "MinorVersionNumber" integer NOT NULL
);


--
-- Name: SeedingHistories_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."SeedingHistories" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."SeedingHistories_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Subscriptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Subscriptions" (
    "Id" bigint NOT NULL,
    "EndDate" timestamp with time zone NOT NULL,
    "StartDate" timestamp with time zone NOT NULL,
    "SubscriptionType" integer NOT NULL,
    "ProfileId" bigint NOT NULL,
    "BusinessUnitId" bigint NOT NULL
);


--
-- Name: Subscriptions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Subscriptions" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."Subscriptions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: SystemModules; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."SystemModules" (
    "ModuleID" integer NOT NULL,
    "ModuleName" text NOT NULL,
    "FriendlyName" text NOT NULL,
    "Description" text
);


--
-- Name: SystemNotification; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."SystemNotification" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Title" character varying(255),
    "Message" text,
    "NotificationType" character varying(100),
    "IpAddress" character varying(50),
    "NotificationUrl" text,
    "IsRead" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "ReadAt" timestamp without time zone
);


--
-- Name: SystemNotification_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."SystemNotification" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."SystemNotification_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamDailyFeedback; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamDailyFeedback" (
    "Id" bigint NOT NULL,
    "BusinessUnitId" bigint NOT NULL,
    "TeamDailyQuestionId" bigint NOT NULL,
    "TeamDailyReasonId" bigint NOT NULL,
    "SentimentType" integer NOT NULL,
    "FreeText" character varying(896),
    "SubmissionDateTime" timestamp with time zone NOT NULL
);


--
-- Name: TeamDailyFeedbackAuditLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamDailyFeedbackAuditLogs" (
    "AuditId" integer NOT NULL,
    "Id" bigint NOT NULL,
    "SubmissionDateTime" timestamp with time zone NOT NULL,
    "BusinessUnitId" bigint NOT NULL,
    "SentimentType" integer NOT NULL,
    "TeamDailyQuestionId" bigint NOT NULL,
    "TeamDailyReasonId" bigint NOT NULL,
    "FreeText" character varying(896),
    "LoggedAt" timestamp with time zone NOT NULL,
    "VersionNumber" integer NOT NULL,
    "OperationType" character varying(20) NOT NULL,
    "RecordCountSnapshot" integer
);


--
-- Name: TeamDailyFeedbackAuditLogs_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamDailyFeedbackAuditLogs" ALTER COLUMN "AuditId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamDailyFeedbackAuditLogs_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamDailyFeedbackReasons; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamDailyFeedbackReasons" (
    "Id" bigint NOT NULL,
    "TeamDailyFeedbackId" bigint NOT NULL,
    "TeamDailyReasonId" bigint NOT NULL
);


--
-- Name: TeamDailyFeedbackReasons_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamDailyFeedbackReasons" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamDailyFeedbackReasons_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamDailyFeedbacks_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamDailyFeedback" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamDailyFeedbacks_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamDailyMicroActions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamDailyMicroActions" (
    "Id" bigint NOT NULL,
    "TeamDailyReasonId" bigint NOT NULL,
    "ContentCultureId" bigint DEFAULT 0 NOT NULL,
    "Text" character varying(1000) DEFAULT ''::character varying NOT NULL
);


--
-- Name: TeamDailyMicroActions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamDailyMicroActions" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamDailyMicroActions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamDailyQuestionSpecifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamDailyQuestionSpecifications" (
    "Id" bigint NOT NULL,
    "TeamDailyQuestionId" bigint NOT NULL,
    "Variant" integer NOT NULL,
    "ContentCultureId" bigint DEFAULT 0 NOT NULL,
    "Ordinal" integer DEFAULT 0 NOT NULL,
    "SubCategory" integer DEFAULT 0 NOT NULL,
    "TeamElement" integer DEFAULT 0 NOT NULL,
    "CategoryId" bigint DEFAULT 0 NOT NULL
);


--
-- Name: TeamDailyQuestionSpecifications_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamDailyQuestionSpecifications" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamDailyQuestionSpecifications_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamDailyQuestions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamDailyQuestions" (
    "Id" bigint NOT NULL,
    "ContentCultureId" bigint NOT NULL,
    "Text" character varying(1000) NOT NULL
);


--
-- Name: TeamDailyQuestions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamDailyQuestions" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamDailyQuestions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamDailyReasons; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamDailyReasons" (
    "Id" bigint NOT NULL,
    "SpecificationId" bigint NOT NULL,
    "TeamDailyReasonSentimentType" integer NOT NULL,
    "Text" character varying(1000) NOT NULL,
    "RequiresFreeTextFeedback" boolean NOT NULL,
    "ContentCultureId" bigint DEFAULT 0 NOT NULL,
    "TeamState" integer
);


--
-- Name: TeamDailyReasons_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamDailyReasons" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamDailyReasons_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamFeedback; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamFeedback" (
    "Id" bigint NOT NULL,
    "BusinessUnitId" bigint NOT NULL,
    "FeedbackText" character varying(300) NOT NULL,
    "ClientSubmittedAtUtc" timestamp with time zone NOT NULL,
    "SubmissionDateTime" timestamp with time zone NOT NULL
);


--
-- Name: TeamFeedback_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamFeedback" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamFeedback_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamGoalAudits; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamGoalAudits" (
    "AuditId" integer NOT NULL,
    "TeamGoalId" bigint NOT NULL,
    "ProfileId" bigint NOT NULL,
    "UnitId" bigint NOT NULL,
    "GoalText" character varying(1000) NOT NULL,
    "DisplayOrder" smallint NOT NULL,
    "TargetDate" date,
    "IsActive" boolean NOT NULL,
    "ChangeSummary" character varying(1000),
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL,
    "LoggedAt" timestamp with time zone NOT NULL,
    "VersionNumber" integer NOT NULL,
    "OperationType" character varying(20) NOT NULL
);


--
-- Name: TeamGoalAudits_AuditId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamGoalAudits" ALTER COLUMN "AuditId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamGoalAudits_AuditId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamGoals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamGoals" (
    "Id" bigint NOT NULL,
    "ProfileId" bigint NOT NULL,
    "UnitId" bigint NOT NULL,
    "GoalText" character varying(1000) NOT NULL,
    "DisplayOrder" smallint NOT NULL,
    "TargetDate" date,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL
);


--
-- Name: TeamGoals_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamGoals" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamGoals_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamQuestionCategories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamQuestionCategories" (
    "Id" bigint NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Description" character varying(1000),
    "IsActive" boolean NOT NULL,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL
);


--
-- Name: TeamQuestionCategories_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamQuestionCategories" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamQuestionCategories_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TeamSetups; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeamSetups" (
    "Id" bigint NOT NULL,
    "ProfileId" bigint NOT NULL,
    "UnitId" bigint NOT NULL,
    "TeamType" integer NOT NULL,
    "Context" character varying(4000) NOT NULL,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsDel" boolean NOT NULL
);


--
-- Name: TeamSetups_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TeamSetups" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TeamSetups_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TestCaseAssertions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TestCaseAssertions" (
    "Id" integer NOT NULL,
    "TestCaseId" integer NOT NULL,
    "Name" character varying(250) NOT NULL,
    "AssertionType" character varying(100) NOT NULL,
    "JsonPath" character varying(500),
    "Operator" character varying(100),
    "ExpectedValue" text,
    "IsDel" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: TestCases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TestCases" (
    "Id" integer NOT NULL,
    "Name" character varying(250) NOT NULL,
    "PromptTemplateName" character varying(150) NOT NULL,
    "InputDataJson" text DEFAULT ''::text NOT NULL,
    "IsDel" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedBy" character varying(256),
    "ModifiedAt" timestamp with time zone,
    "ModifiedBy" character varying(256)
);


--
-- Name: TimeAllocation; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TimeAllocation" (
    "Id" bigint NOT NULL,
    "DailyReflectionId" bigint NOT NULL,
    "OptionId" integer DEFAULT 0 NOT NULL,
    "Hours" numeric(5,2) DEFAULT 0 NOT NULL
);


--
-- Name: TimeAllocation_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TimeAllocation" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."TimeAllocation_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TopActions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TopActions" (
    "Id" integer NOT NULL,
    "UnitId" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "Action" text NOT NULL,
    "ReportId" bigint DEFAULT 0
);


--
-- Name: TopActions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TopActions" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."TopActions_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TopRecommendations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TopRecommendations" (
    "Id" integer NOT NULL,
    "UnitId" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "Recommendation" text NOT NULL,
    "ReportId" bigint DEFAULT 0
);


--
-- Name: TopRecommendations_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TopRecommendations" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."TopRecommendations_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: TopThemes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TopThemes" (
    "Id" integer NOT NULL,
    "UnitId" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "Theme" text NOT NULL,
    "ReportId" bigint DEFAULT 0
);


--
-- Name: TopThemes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."TopThemes" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."TopThemes_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: UserGroups; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserGroups" (
    "Id" integer NOT NULL,
    "Name" text NOT NULL,
    "IsAdmin" boolean NOT NULL,
    "UnitId" integer NOT NULL,
    "OrganizationId" integer,
    "CreatedBy" text,
    "Description" text
);


--
-- Name: UserRoles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserRoles" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "UserGroupId" integer NOT NULL,
    "CreatedBy" integer,
    "CreatedDate" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: UserRoles_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."UserRoles" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."UserRoles_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: UserTeams; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserTeams" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "CreatedBy" integer NOT NULL,
    "CreatedDate" timestamp with time zone NOT NULL,
    "UnitId" bigint DEFAULT 0 NOT NULL,
    "IsUnAssigned" boolean,
    "IsReadOnly" boolean DEFAULT false,
    "IsNewTeam" boolean DEFAULT false NOT NULL,
    "IsPendingActivation" boolean DEFAULT false NOT NULL
);


--
-- Name: UserTeams_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."UserTeams" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."UserTeams_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: UserTokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserTokens" (
    "AuthId" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Token" text NOT NULL,
    "ExpiryDate" timestamp with time zone NOT NULL
);


--
-- Name: UserTokens_AuthId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."UserTokens" ALTER COLUMN "AuthId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."UserTokens_AuthId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Users" (
    "UserId" integer NOT NULL,
    "TenantId" text NOT NULL,
    "Username" text NOT NULL,
    "Name" text NOT NULL,
    "CreatedDate" text,
    "ModifiedDate" text DEFAULT '-infinity'::timestamp with time zone,
    "CreatedBy" text,
    "ModifiedBy" text,
    "IsDel" boolean,
    "IsActive" boolean,
    "LastLoginDate" timestamp without time zone,
    "IsVerified" boolean DEFAULT false NOT NULL,
    "Password" text DEFAULT ''::text,
    "Status" text DEFAULT ''::text NOT NULL,
    "Type" text DEFAULT ''::text NOT NULL,
    "IsEntraId" boolean DEFAULT false NOT NULL,
    "IsFirstTimePasswordChange" boolean,
    "ProfileId" integer,
    "FailedLoginAttempts" integer DEFAULT 0 NOT NULL,
    "LastFailedLoginAt" timestamp without time zone,
    "UserGroupId" integer NOT NULL
);


--
-- Name: Users_UserId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Users" ALTER COLUMN "UserId" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Users_UserId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: WorkbenchRunStatuses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."WorkbenchRunStatuses" (
    "RunId" integer NOT NULL,
    "Status" character varying(20) DEFAULT 'Queued'::character varying NOT NULL,
    "ResultJson" text,
    "ErrorMessage" text,
    "DetailedLogs" text,
    "CreatedAt" timestamp with time zone DEFAULT (now() AT TIME ZONE 'utc'::text) NOT NULL,
    "CompletedAt" timestamp with time zone
);


--
-- Name: WorkbenchRunStatuses_RunId_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."WorkbenchRunStatuses" ALTER COLUMN "RunId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."WorkbenchRunStatuses_RunId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


--
-- Name: workcategories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.workcategories (
    id integer NOT NULL,
    category text NOT NULL,
    title text NOT NULL,
    subtitle text NOT NULL
);


--
-- Name: workcategories_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.workcategories ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.workcategories_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: AzureMarketplaceSubscription Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AzureMarketplaceSubscription" ALTER COLUMN "Id" SET DEFAULT nextval('public."AzureMarketplaceSubscription_Id_seq"'::regclass);


--
-- Name: AzureMarketplaceWebhookEvent Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AzureMarketplaceWebhookEvent" ALTER COLUMN "Id" SET DEFAULT nextval('public."AzureMarketplaceWebhookEvent_Id_seq"'::regclass);


--
-- Name: InsightPublishLogs Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."InsightPublishLogs" ALTER COLUMN "Id" SET DEFAULT nextval('public."InsightPublishLogs_Id_seq"'::regclass);


--
-- Name: PromoCodeUsages Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsages" ALTER COLUMN "Id" SET DEFAULT nextval('public."PromoCodeUsages_Id_seq"'::regclass);


--
-- Name: PromoCodes Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodes" ALTER COLUMN "Id" SET DEFAULT nextval('public."PromoCodes_Id_seq"'::regclass);


--
-- Name: AIModelConfigs AIModelConfigs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AIModelConfigs"
    ADD CONSTRAINT "AIModelConfigs_pkey" PRIMARY KEY ("Id");


--
-- Name: AIRequestLogs AIRequestLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AIRequestLogs"
    ADD CONSTRAINT "AIRequestLogs_pkey" PRIMARY KEY ("Id");


--
-- Name: AzureMarketplaceSubscription AzureMarketplaceSubscription_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AzureMarketplaceSubscription"
    ADD CONSTRAINT "AzureMarketplaceSubscription_pkey" PRIMARY KEY ("Id");


--
-- Name: AzureMarketplaceWebhookEvent AzureMarketplaceWebhookEvent_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AzureMarketplaceWebhookEvent"
    ADD CONSTRAINT "AzureMarketplaceWebhookEvent_pkey" PRIMARY KEY ("Id");


--
-- Name: CompanySubscriptionAuditLogs CompanySubscriptionAuditLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanySubscriptionAuditLogs"
    ADD CONSTRAINT "CompanySubscriptionAuditLogs_pkey" PRIMARY KEY ("AuditId");


--
-- Name: CompanySubscriptions CompanySubscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanySubscriptions"
    ADD CONSTRAINT "CompanySubscriptions_pkey" PRIMARY KEY ("Id");


--
-- Name: DailyFeedbackAuditLogs DailyFeedbackAuditLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyFeedbackAuditLogs"
    ADD CONSTRAINT "DailyFeedbackAuditLogs_pkey" PRIMARY KEY ("AuditId");


--
-- Name: DailyReflections DailyReflections_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyReflections"
    ADD CONSTRAINT "DailyReflections_pkey" PRIMARY KEY ("Id");


--
-- Name: DocumentEmbeddingAuditLogs DocumentEmbeddingAuditLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DocumentEmbeddingAuditLogs"
    ADD CONSTRAINT "DocumentEmbeddingAuditLogs_pkey" PRIMARY KEY ("Id");


--
-- Name: InsightPublishLogs InsightPublishLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."InsightPublishLogs"
    ADD CONSTRAINT "InsightPublishLogs_pkey" PRIMARY KEY ("Id");


--
-- Name: LLMReports LLMReports_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LLMReports"
    ADD CONSTRAINT "LLMReports_pkey" PRIMARY KEY ("Id");


--
-- Name: ModelConfiguration ModelConfiguration_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ModelConfiguration"
    ADD CONSTRAINT "ModelConfiguration_pkey" PRIMARY KEY ("Id");


--
-- Name: Opportunities Opportunities_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Opportunities"
    ADD CONSTRAINT "Opportunities_pkey" PRIMARY KEY ("Id");


--
-- Name: ActionEvents PK_ActionEvents; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ActionEvents"
    ADD CONSTRAINT "PK_ActionEvents" PRIMARY KEY ("Id");


--
-- Name: ActionHubs PK_ActionHubs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ActionHubs"
    ADD CONSTRAINT "PK_ActionHubs" PRIMARY KEY ("Id");


--
-- Name: AssignUserRights PK_AssignUserRights; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AssignUserRights"
    ADD CONSTRAINT "PK_AssignUserRights" PRIMARY KEY ("AssignRightId");


--
-- Name: AssignedCompanyLicenseAuditLogs PK_AssignedCompanyLicenseAuditLogs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AssignedCompanyLicenseAuditLogs"
    ADD CONSTRAINT "PK_AssignedCompanyLicenseAuditLogs" PRIMARY KEY ("AuditId");


--
-- Name: AssignedCompanyLicenses PK_AssignedCompanyLicenses; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AssignedCompanyLicenses"
    ADD CONSTRAINT "PK_AssignedCompanyLicenses" PRIMARY KEY ("Id");


--
-- Name: AuditLogs PK_AuditLogs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AuditLogs"
    ADD CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("LogId");


--
-- Name: BusinessUnits PK_BusinessUnits; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BusinessUnits"
    ADD CONSTRAINT "PK_BusinessUnits" PRIMARY KEY ("Id");


--
-- Name: CloveraPersonalSnapshots PK_CloveraPersonalSnapshots; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CloveraPersonalSnapshots"
    ADD CONSTRAINT "PK_CloveraPersonalSnapshots" PRIMARY KEY ("Id");


--
-- Name: CompanyLicenseAuditLogs PK_CompanyLicenseAuditLogs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanyLicenseAuditLogs"
    ADD CONSTRAINT "PK_CompanyLicenseAuditLogs" PRIMARY KEY ("AuditId");


--
-- Name: CompanyLicenses PK_CompanyLicenses; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanyLicenses"
    ADD CONSTRAINT "PK_CompanyLicenses" PRIMARY KEY ("Id");


--
-- Name: CompanyLinks PK_CompanyLinks; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanyLinks"
    ADD CONSTRAINT "PK_CompanyLinks" PRIMARY KEY ("CompanyLinkId");


--
-- Name: CompanyTeams PK_CompanyTeams; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanyTeams"
    ADD CONSTRAINT "PK_CompanyTeams" PRIMARY KEY ("TeamId");


--
-- Name: ComplimentaryLicenseGrantAuditLogs PK_ComplimentaryLicenseGrantAuditLogs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ComplimentaryLicenseGrantAuditLogs"
    ADD CONSTRAINT "PK_ComplimentaryLicenseGrantAuditLogs" PRIMARY KEY ("AuditId");


--
-- Name: ComplimentaryLicenseGrants PK_ComplimentaryLicenseGrants; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ComplimentaryLicenseGrants"
    ADD CONSTRAINT "PK_ComplimentaryLicenseGrants" PRIMARY KEY ("Id");


--
-- Name: ContentCultures PK_ContentCultures; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ContentCultures"
    ADD CONSTRAINT "PK_ContentCultures" PRIMARY KEY ("Id");


--
-- Name: CustomQuestionAudits PK_CustomQuestionAudits; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomQuestionAudits"
    ADD CONSTRAINT "PK_CustomQuestionAudits" PRIMARY KEY ("AuditId");


--
-- Name: CustomQuestionDailyFeedback PK_CustomQuestionDailyFeedback; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomQuestionDailyFeedback"
    ADD CONSTRAINT "PK_CustomQuestionDailyFeedback" PRIMARY KEY ("Id");


--
-- Name: CustomQuestionDailyFeedbackAudits PK_CustomQuestionDailyFeedbackAudits; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomQuestionDailyFeedbackAudits"
    ADD CONSTRAINT "PK_CustomQuestionDailyFeedbackAudits" PRIMARY KEY ("AuditId");


--
-- Name: CustomQuestions PK_CustomQuestions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomQuestions"
    ADD CONSTRAINT "PK_CustomQuestions" PRIMARY KEY ("Id");


--
-- Name: DailyFeedback PK_DailyFeedback; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyFeedback"
    ADD CONSTRAINT "PK_DailyFeedback" PRIMARY KEY ("Id");


--
-- Name: DailyFeedbackReasons PK_DailyFeedbackReasons; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyFeedbackReasons"
    ADD CONSTRAINT "PK_DailyFeedbackReasons" PRIMARY KEY ("Id");


--
-- Name: DailyMicroActions PK_DailyMicroActions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyMicroActions"
    ADD CONSTRAINT "PK_DailyMicroActions" PRIMARY KEY ("Id");


--
-- Name: DailyQuestionSetGroups PK_DailyQuestionSetGroups; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestionSetGroups"
    ADD CONSTRAINT "PK_DailyQuestionSetGroups" PRIMARY KEY ("Id");


--
-- Name: DailyQuestionSets PK_DailyQuestionSets; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestionSets"
    ADD CONSTRAINT "PK_DailyQuestionSets" PRIMARY KEY ("Id");


--
-- Name: DailyQuestionSpecifications PK_DailyQuestionSpecifications; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestionSpecifications"
    ADD CONSTRAINT "PK_DailyQuestionSpecifications" PRIMARY KEY ("Id");


--
-- Name: DailyQuestions PK_DailyQuestions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestions"
    ADD CONSTRAINT "PK_DailyQuestions" PRIMARY KEY ("Id");


--
-- Name: DailyReasons PK_DailyReasons; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyReasons"
    ADD CONSTRAINT "PK_DailyReasons" PRIMARY KEY ("Id");


--
-- Name: DocumentEmbeddings PK_DocumentEmbeddings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DocumentEmbeddings"
    ADD CONSTRAINT "PK_DocumentEmbeddings" PRIMARY KEY ("Id");


--
-- Name: EranCoachConversations PK_EranCoachConversations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."EranCoachConversations"
    ADD CONSTRAINT "PK_EranCoachConversations" PRIMARY KEY ("Id");


--
-- Name: EranCoachManagerMemories PK_EranCoachManagerMemories; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."EranCoachManagerMemories"
    ADD CONSTRAINT "PK_EranCoachManagerMemories" PRIMARY KEY ("Id");


--
-- Name: EranCoachMessages PK_EranCoachMessages; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."EranCoachMessages"
    ADD CONSTRAINT "PK_EranCoachMessages" PRIMARY KEY ("Id");


--
-- Name: FeedBack PK_FeedBack; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FeedBack"
    ADD CONSTRAINT "PK_FeedBack" PRIMARY KEY ("Id");


--
-- Name: FeedbackAnalysisResults PK_FeedbackAnalysisResults; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FeedbackAnalysisResults"
    ADD CONSTRAINT "PK_FeedbackAnalysisResults" PRIMARY KEY ("Id");


--
-- Name: Notifications PK_Notifications; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Notifications"
    ADD CONSTRAINT "PK_Notifications" PRIMARY KEY ("NotificationId");


--
-- Name: PortfolioManagerAssignments PK_PortfolioManagerAssignments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PortfolioManagerAssignments"
    ADD CONSTRAINT "PK_PortfolioManagerAssignments" PRIMARY KEY ("Id");


--
-- Name: Profiles PK_Profiles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Profiles"
    ADD CONSTRAINT "PK_Profiles" PRIMARY KEY ("Id");


--
-- Name: PromptTypes PK_PromptTypes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromptTypes"
    ADD CONSTRAINT "PK_PromptTypes" PRIMARY KEY ("PromptTypeId");


--
-- Name: SeedingHistories PK_SeedingHistories; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."SeedingHistories"
    ADD CONSTRAINT "PK_SeedingHistories" PRIMARY KEY ("Id");


--
-- Name: Subscriptions PK_Subscriptions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subscriptions"
    ADD CONSTRAINT "PK_Subscriptions" PRIMARY KEY ("Id");


--
-- Name: SystemModules PK_SystemModules; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."SystemModules"
    ADD CONSTRAINT "PK_SystemModules" PRIMARY KEY ("ModuleID");


--
-- Name: TeamDailyFeedback PK_TeamDailyFeedback; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyFeedback"
    ADD CONSTRAINT "PK_TeamDailyFeedback" PRIMARY KEY ("Id");


--
-- Name: TeamDailyFeedbackAuditLogs PK_TeamDailyFeedbackAuditLogs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyFeedbackAuditLogs"
    ADD CONSTRAINT "PK_TeamDailyFeedbackAuditLogs" PRIMARY KEY ("AuditId");


--
-- Name: TeamDailyFeedbackReasons PK_TeamDailyFeedbackReasons; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyFeedbackReasons"
    ADD CONSTRAINT "PK_TeamDailyFeedbackReasons" PRIMARY KEY ("Id");


--
-- Name: TeamDailyMicroActions PK_TeamDailyMicroActions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyMicroActions"
    ADD CONSTRAINT "PK_TeamDailyMicroActions" PRIMARY KEY ("Id");


--
-- Name: TeamDailyQuestionSpecifications PK_TeamDailyQuestionSpecifications; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyQuestionSpecifications"
    ADD CONSTRAINT "PK_TeamDailyQuestionSpecifications" PRIMARY KEY ("Id");


--
-- Name: TeamDailyQuestions PK_TeamDailyQuestions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyQuestions"
    ADD CONSTRAINT "PK_TeamDailyQuestions" PRIMARY KEY ("Id");


--
-- Name: TeamDailyReasons PK_TeamDailyReasons; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyReasons"
    ADD CONSTRAINT "PK_TeamDailyReasons" PRIMARY KEY ("Id");


--
-- Name: TeamFeedback PK_TeamFeedback; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamFeedback"
    ADD CONSTRAINT "PK_TeamFeedback" PRIMARY KEY ("Id");


--
-- Name: TeamGoalAudits PK_TeamGoalAudits; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamGoalAudits"
    ADD CONSTRAINT "PK_TeamGoalAudits" PRIMARY KEY ("AuditId");


--
-- Name: TeamGoals PK_TeamGoals; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamGoals"
    ADD CONSTRAINT "PK_TeamGoals" PRIMARY KEY ("Id");


--
-- Name: TeamQuestionCategories PK_TeamQuestionCategories; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamQuestionCategories"
    ADD CONSTRAINT "PK_TeamQuestionCategories" PRIMARY KEY ("Id");


--
-- Name: TeamSetups PK_TeamSetups; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamSetups"
    ADD CONSTRAINT "PK_TeamSetups" PRIMARY KEY ("Id");


--
-- Name: TestCaseAssertions PK_TestCaseAssertions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TestCaseAssertions"
    ADD CONSTRAINT "PK_TestCaseAssertions" PRIMARY KEY ("Id");


--
-- Name: TestCases PK_TestCases; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TestCases"
    ADD CONSTRAINT "PK_TestCases" PRIMARY KEY ("Id");


--
-- Name: TimeAllocation PK_TimeEntryBand; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TimeAllocation"
    ADD CONSTRAINT "PK_TimeEntryBand" PRIMARY KEY ("Id");


--
-- Name: TopActions PK_TopActions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TopActions"
    ADD CONSTRAINT "PK_TopActions" PRIMARY KEY ("Id");


--
-- Name: TopRecommendations PK_TopRecommendations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TopRecommendations"
    ADD CONSTRAINT "PK_TopRecommendations" PRIMARY KEY ("Id");


--
-- Name: TopThemes PK_TopThemes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TopThemes"
    ADD CONSTRAINT "PK_TopThemes" PRIMARY KEY ("Id");


--
-- Name: UserGroups PK_UserGroups; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserGroups"
    ADD CONSTRAINT "PK_UserGroups" PRIMARY KEY ("Id");


--
-- Name: UserRoles PK_UserRoles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserRoles"
    ADD CONSTRAINT "PK_UserRoles" PRIMARY KEY ("Id");


--
-- Name: UserTeams PK_UserTeams; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserTeams"
    ADD CONSTRAINT "PK_UserTeams" PRIMARY KEY ("Id");


--
-- Name: UserTokens PK_UserTokens; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserTokens"
    ADD CONSTRAINT "PK_UserTokens" PRIMARY KEY ("AuthId");


--
-- Name: Users PK_Users; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "PK_Users" PRIMARY KEY ("UserId");


--
-- Name: WorkbenchRunStatuses PK_WorkbenchRunStatuses; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."WorkbenchRunStatuses"
    ADD CONSTRAINT "PK_WorkbenchRunStatuses" PRIMARY KEY ("RunId");


--
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- Name: PartnerCustomerAuditLogs PartnerCustomerAuditLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PartnerCustomerAuditLogs"
    ADD CONSTRAINT "PartnerCustomerAuditLogs_pkey" PRIMARY KEY ("Id");


--
-- Name: PaymentAuditLogs PaymentAuditLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PaymentAuditLogs"
    ADD CONSTRAINT "PaymentAuditLogs_pkey" PRIMARY KEY ("AuditId");


--
-- Name: Payments Payments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Payments"
    ADD CONSTRAINT "Payments_pkey" PRIMARY KEY ("Id");


--
-- Name: Plans Plans_Slug_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Plans"
    ADD CONSTRAINT "Plans_Slug_key" UNIQUE ("Slug");


--
-- Name: Plans Plans_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Plans"
    ADD CONSTRAINT "Plans_pkey" PRIMARY KEY ("Id");


--
-- Name: PromoCodeAuditLogs PromoCodeAuditLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeAuditLogs"
    ADD CONSTRAINT "PromoCodeAuditLogs_pkey" PRIMARY KEY ("AuditId");


--
-- Name: PromoCodeUsageAuditLogs PromoCodeUsageAuditLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsageAuditLogs"
    ADD CONSTRAINT "PromoCodeUsageAuditLogs_pkey" PRIMARY KEY ("AuditId");


--
-- Name: PromoCodeUsages PromoCodeUsages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "PromoCodeUsages_pkey" PRIMARY KEY ("Id");


--
-- Name: PromoCodes PromoCodes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodes"
    ADD CONSTRAINT "PromoCodes_pkey" PRIMARY KEY ("Id");


--
-- Name: PromptConfiguration PromptConfiguration_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromptConfiguration"
    ADD CONSTRAINT "PromptConfiguration_pkey" PRIMARY KEY ("Id");


--
-- Name: PromptTemplates PromptTemplates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromptTemplates"
    ADD CONSTRAINT "PromptTemplates_pkey" PRIMARY KEY ("Id");


--
-- Name: SystemNotification SystemNotification_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."SystemNotification"
    ADD CONSTRAINT "SystemNotification_pkey" PRIMARY KEY ("Id");


--
-- Name: workcategories workcategories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.workcategories
    ADD CONSTRAINT workcategories_pkey PRIMARY KEY (id);


--
-- Name: IX_AssignedCompanyLicenseAuditLogs_Id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_AssignedCompanyLicenseAuditLogs_Id" ON public."AssignedCompanyLicenseAuditLogs" USING btree ("Id");


--
-- Name: IX_AssignedCompanyLicenses_BusinessUnitId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_AssignedCompanyLicenses_BusinessUnitId" ON public."AssignedCompanyLicenses" USING btree ("BusinessUnitId");


--
-- Name: IX_AssignedCompanyLicenses_CompanyLicenseId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_AssignedCompanyLicenses_CompanyLicenseId" ON public."AssignedCompanyLicenses" USING btree ("CompanyLicenseId");


--
-- Name: IX_AssignedCompanyLicenses_CompanyLicenseId_IsActive; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_AssignedCompanyLicenses_CompanyLicenseId_IsActive" ON public."AssignedCompanyLicenses" USING btree ("CompanyLicenseId", "IsActive") WHERE ("IsActive" = true);


--
-- Name: IX_AzureMarketplaceSubscription_MarketplaceSubscriptionId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_AzureMarketplaceSubscription_MarketplaceSubscriptionId" ON public."AzureMarketplaceSubscription" USING btree ("MarketplaceSubscriptionId");


--
-- Name: IX_AzureMarketplaceSubscription_ProfileId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_AzureMarketplaceSubscription_ProfileId" ON public."AzureMarketplaceSubscription" USING btree ("ProfileId");


--
-- Name: IX_AzureMarketplaceWebhookEvent_MarketplaceSubscriptionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_AzureMarketplaceWebhookEvent_MarketplaceSubscriptionId" ON public."AzureMarketplaceWebhookEvent" USING btree ("MarketplaceSubscriptionId");


--
-- Name: IX_AzureMarketplaceWebhookEvent_OperationId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_AzureMarketplaceWebhookEvent_OperationId" ON public."AzureMarketplaceWebhookEvent" USING btree ("OperationId");


--
-- Name: IX_BusinessUnits_ActiveDailyQuestionSetGroupId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_BusinessUnits_ActiveDailyQuestionSetGroupId" ON public."BusinessUnits" USING btree ("ActiveDailyQuestionSetGroupId");


--
-- Name: IX_BusinessUnits_BusinessUnitCode; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_BusinessUnits_BusinessUnitCode" ON public."BusinessUnits" USING btree ("BusinessUnitCode");


--
-- Name: IX_BusinessUnits_BusinessUnitShortCode; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_BusinessUnits_BusinessUnitShortCode" ON public."BusinessUnits" USING btree ("BusinessUnitShortCode");


--
-- Name: IX_BusinessUnits_ProfileId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_BusinessUnits_ProfileId" ON public."BusinessUnits" USING btree ("ProfileId");


--
-- Name: IX_CompanyLicenseAuditLogs_Id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CompanyLicenseAuditLogs_Id" ON public."CompanyLicenseAuditLogs" USING btree ("Id");


--
-- Name: IX_CompanyLicenses_CompanySubscriptionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CompanyLicenses_CompanySubscriptionId" ON public."CompanyLicenses" USING btree ("CompanySubscriptionId");


--
-- Name: IX_CompanyLicenses_LicenseKey; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_CompanyLicenses_LicenseKey" ON public."CompanyLicenses" USING btree ("LicenseKey");


--
-- Name: IX_CompanyLicenses_PlanId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CompanyLicenses_PlanId" ON public."CompanyLicenses" USING btree ("PlanId");


--
-- Name: IX_CompanyLicenses_ProfileId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CompanyLicenses_ProfileId" ON public."CompanyLicenses" USING btree ("ProfileId");


--
-- Name: IX_CompanySubscriptions_PlanId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CompanySubscriptions_PlanId" ON public."CompanySubscriptions" USING btree ("PlanId");


--
-- Name: IX_CompanySubscriptions_ProfileId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CompanySubscriptions_ProfileId" ON public."CompanySubscriptions" USING btree ("ProfileId");


--
-- Name: IX_ComplimentaryLicenseGrants_CompanyLicenseId_IsActive; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ComplimentaryLicenseGrants_CompanyLicenseId_IsActive" ON public."ComplimentaryLicenseGrants" USING btree ("CompanyLicenseId", "IsActive") WHERE ("IsActive" = true);


--
-- Name: IX_ComplimentaryLicenseGrants_ComplimentaryBusinessUnitId_IsAct; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_ComplimentaryLicenseGrants_ComplimentaryBusinessUnitId_IsAct" ON public."ComplimentaryLicenseGrants" USING btree ("ComplimentaryBusinessUnitId", "IsActive") WHERE ("IsActive" = true);


--
-- Name: IX_ComplimentaryLicenseGrants_PrimaryBusinessUnitId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ComplimentaryLicenseGrants_PrimaryBusinessUnitId" ON public."ComplimentaryLicenseGrants" USING btree ("PrimaryBusinessUnitId");


--
-- Name: IX_ContentCultures_CultureName; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_ContentCultures_CultureName" ON public."ContentCultures" USING btree ("CultureName");


--
-- Name: IX_CustomQuestionAudits_Operation; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestionAudits_Operation" ON public."CustomQuestionAudits" USING btree ("OperationType");


--
-- Name: IX_CustomQuestionAudits_Question_LoggedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestionAudits_Question_LoggedAt" ON public."CustomQuestionAudits" USING btree ("CustomQuestionId", "LoggedAt");


--
-- Name: IX_CustomQuestionDailyFeedbackAudits_Feedback_Version; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestionDailyFeedbackAudits_Feedback_Version" ON public."CustomQuestionDailyFeedbackAudits" USING btree ("CustomQuestionDailyFeedbackId", "VersionNumber");


--
-- Name: IX_CustomQuestionDailyFeedbackAudits_Operation; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestionDailyFeedbackAudits_Operation" ON public."CustomQuestionDailyFeedbackAudits" USING btree ("OperationType");


--
-- Name: IX_CustomQuestionDailyFeedback_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestionDailyFeedback_CreatedAt" ON public."CustomQuestionDailyFeedback" USING btree ("CreatedAt");


--
-- Name: IX_CustomQuestionDailyFeedback_Question; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestionDailyFeedback_Question" ON public."CustomQuestionDailyFeedback" USING btree ("CustomQuestionId");


--
-- Name: IX_CustomQuestionDailyFeedback_Unit_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestionDailyFeedback_Unit_CreatedAt" ON public."CustomQuestionDailyFeedback" USING btree ("UnitId", "CreatedAt");


--
-- Name: IX_CustomQuestions_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestions_ContentCultureId" ON public."CustomQuestions" USING btree ("ContentCultureId");


--
-- Name: IX_CustomQuestions_Profile_Unit; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestions_Profile_Unit" ON public."CustomQuestions" USING btree ("ProfileId", "UnitId") WHERE ("IsDel" = false);


--
-- Name: IX_CustomQuestions_Status_ScheduledAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestions_Status_ScheduledAt" ON public."CustomQuestions" USING btree ("Status", "ScheduledAt");


--
-- Name: IX_CustomQuestions_UnitId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CustomQuestions_UnitId" ON public."CustomQuestions" USING btree ("UnitId");


--
-- Name: IX_DailyFeedbackReasons_DailyFeedbackId_DailyReasonId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_DailyFeedbackReasons_DailyFeedbackId_DailyReasonId" ON public."DailyFeedbackReasons" USING btree ("DailyFeedbackId", "DailyReasonId");


--
-- Name: IX_DailyFeedbackReasons_DailyReasonId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyFeedbackReasons_DailyReasonId" ON public."DailyFeedbackReasons" USING btree ("DailyReasonId");


--
-- Name: IX_DailyFeedback_BusinessUnitId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyFeedback_BusinessUnitId" ON public."DailyFeedback" USING btree ("BusinessUnitId");


--
-- Name: IX_DailyFeedback_DailyQuestionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyFeedback_DailyQuestionId" ON public."DailyFeedback" USING btree ("DailyQuestionId");


--
-- Name: IX_DailyFeedback_DailyReasonId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyFeedback_DailyReasonId" ON public."DailyFeedback" USING btree ("DailyReasonId");


--
-- Name: IX_DailyMicroActions_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyMicroActions_ContentCultureId" ON public."DailyMicroActions" USING btree ("ContentCultureId");


--
-- Name: IX_DailyMicroActions_ReasonId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyMicroActions_ReasonId" ON public."DailyMicroActions" USING btree ("ReasonId");


--
-- Name: IX_DailyQuestionSets_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyQuestionSets_ContentCultureId" ON public."DailyQuestionSets" USING btree ("ContentCultureId");


--
-- Name: IX_DailyQuestionSets_DailyQuestionSetGroupId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyQuestionSets_DailyQuestionSetGroupId" ON public."DailyQuestionSets" USING btree ("DailyQuestionSetGroupId");


--
-- Name: IX_DailyQuestionSets_Name_VersionNumber; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_DailyQuestionSets_Name_VersionNumber" ON public."DailyQuestionSets" USING btree ("Name", "VersionNumber");


--
-- Name: IX_DailyQuestionSpecifications_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyQuestionSpecifications_ContentCultureId" ON public."DailyQuestionSpecifications" USING btree ("ContentCultureId");


--
-- Name: IX_DailyQuestionSpecifications_DailyQuestionSetGroupId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyQuestionSpecifications_DailyQuestionSetGroupId" ON public."DailyQuestionSpecifications" USING btree ("DailyQuestionSetGroupId");


--
-- Name: IX_DailyQuestionSpecifications_DailyQuestionSetId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyQuestionSpecifications_DailyQuestionSetId" ON public."DailyQuestionSpecifications" USING btree ("DailyQuestionSetId");


--
-- Name: IX_DailyQuestionSpecifications_QuestionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyQuestionSpecifications_QuestionId" ON public."DailyQuestionSpecifications" USING btree ("QuestionId");


--
-- Name: IX_DailyQuestions_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyQuestions_ContentCultureId" ON public."DailyQuestions" USING btree ("ContentCultureId");


--
-- Name: IX_DailyReasons_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyReasons_ContentCultureId" ON public."DailyReasons" USING btree ("ContentCultureId");


--
-- Name: IX_DailyReasons_SpecificationId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyReasons_SpecificationId" ON public."DailyReasons" USING btree ("SpecificationId");


--
-- Name: IX_DailyReflections_BusinessUnitId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyReflections_BusinessUnitId" ON public."DailyReflections" USING btree ("BusinessUnitId");


--
-- Name: IX_DailyReflections_Unit_Date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DailyReflections_Unit_Date" ON public."DailyReflections" USING btree ("BusinessUnitId", "ReflectionLocalDate");


--
-- Name: IX_DocumentEmbeddings_Embedding; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DocumentEmbeddings_Embedding" ON public."DocumentEmbeddings" USING hnsw ("Embedding" public.vector_cosine_ops);


--
-- Name: IX_EranCoachConversations_UserId_IsDel_LastMessageAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_EranCoachConversations_UserId_IsDel_LastMessageAt" ON public."EranCoachConversations" USING btree ("UserId", "IsDel", "LastMessageAt");


--
-- Name: IX_EranCoachMessages_ConversationId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_EranCoachMessages_ConversationId_CreatedAt" ON public."EranCoachMessages" USING btree ("ConversationId", "CreatedAt");


--
-- Name: IX_FeedBack_Unit; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_FeedBack_Unit" ON public."FeedBack" USING btree ("BusinessUnitId");


--
-- Name: IX_InsightPublishLogs_ReportId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_InsightPublishLogs_ReportId" ON public."InsightPublishLogs" USING btree ("ReportId");


--
-- Name: IX_InsightPublishLogs_RunId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_InsightPublishLogs_RunId" ON public."InsightPublishLogs" USING btree ("RunId");


--
-- Name: IX_Payments_CompanySubscriptionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Payments_CompanySubscriptionId" ON public."Payments" USING btree ("CompanySubscriptionId");


--
-- Name: IX_Payments_ProfileId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Payments_ProfileId" ON public."Payments" USING btree ("ProfileId");


--
-- Name: IX_PortfolioManagerAssignments_PortfolioManager_IsUnAssigned; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PortfolioManagerAssignments_PortfolioManager_IsUnAssigned" ON public."PortfolioManagerAssignments" USING btree ("PortfolioManagerUserId", "IsUnAssigned");


--
-- Name: IX_PortfolioManagerAssignments_ProfileId_IsUnAssigned; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_PortfolioManagerAssignments_ProfileId_IsUnAssigned" ON public."PortfolioManagerAssignments" USING btree ("ProfileId", "IsUnAssigned");


--
-- Name: IX_Profiles_ParentId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Profiles_ParentId" ON public."Profiles" USING btree ("ParentId");


--
-- Name: IX_Profiles_PartnerId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Profiles_PartnerId" ON public."Profiles" USING btree ("PartnerId");


--
-- Name: IX_Profiles_ProfileName; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Profiles_ProfileName" ON public."Profiles" USING btree ("ProfileName");


--
-- Name: IX_Profiles_ProfileShortCode; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Profiles_ProfileShortCode" ON public."Profiles" USING btree ("ProfileShortCode");


--
-- Name: IX_PromoCodes_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_PromoCodes_Code" ON public."PromoCodes" USING btree ("Code");


--
-- Name: IX_SeedingHistories_MajorVersionNumber_MinorVersionNumber; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_SeedingHistories_MajorVersionNumber_MinorVersionNumber" ON public."SeedingHistories" USING btree ("MajorVersionNumber", "MinorVersionNumber");


--
-- Name: IX_Subscriptions_BusinessUnitId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Subscriptions_BusinessUnitId" ON public."Subscriptions" USING btree ("BusinessUnitId");


--
-- Name: IX_Subscriptions_ProfileId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Subscriptions_ProfileId" ON public."Subscriptions" USING btree ("ProfileId");


--
-- Name: IX_TeamDailyFeedbackAuditLogs_Id_Version; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyFeedbackAuditLogs_Id_Version" ON public."TeamDailyFeedbackAuditLogs" USING btree ("Id", "VersionNumber");


--
-- Name: IX_TeamDailyFeedbackAuditLogs_Operation; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyFeedbackAuditLogs_Operation" ON public."TeamDailyFeedbackAuditLogs" USING btree ("OperationType");


--
-- Name: IX_TeamDailyFeedbackReasons_TeamDailyFeedbackId_TeamDailyReaso~; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_TeamDailyFeedbackReasons_TeamDailyFeedbackId_TeamDailyReaso~" ON public."TeamDailyFeedbackReasons" USING btree ("TeamDailyFeedbackId", "TeamDailyReasonId");


--
-- Name: IX_TeamDailyFeedbackReasons_TeamDailyReasonId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyFeedbackReasons_TeamDailyReasonId" ON public."TeamDailyFeedbackReasons" USING btree ("TeamDailyReasonId");


--
-- Name: IX_TeamDailyFeedback_Question; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyFeedback_Question" ON public."TeamDailyFeedback" USING btree ("TeamDailyQuestionId");


--
-- Name: IX_TeamDailyFeedback_Reason; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyFeedback_Reason" ON public."TeamDailyFeedback" USING btree ("TeamDailyReasonId");


--
-- Name: IX_TeamDailyFeedback_Unit_Submitted; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyFeedback_Unit_Submitted" ON public."TeamDailyFeedback" USING btree ("BusinessUnitId", "SubmissionDateTime");


--
-- Name: IX_TeamDailyMicroActions_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyMicroActions_ContentCultureId" ON public."TeamDailyMicroActions" USING btree ("ContentCultureId");


--
-- Name: IX_TeamDailyMicroActions_Reason; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyMicroActions_Reason" ON public."TeamDailyMicroActions" USING btree ("TeamDailyReasonId");


--
-- Name: IX_TeamDailyQuestionSpecifications_Category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyQuestionSpecifications_Category" ON public."TeamDailyQuestionSpecifications" USING btree ("CategoryId");


--
-- Name: IX_TeamDailyQuestionSpecifications_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyQuestionSpecifications_ContentCultureId" ON public."TeamDailyQuestionSpecifications" USING btree ("ContentCultureId");


--
-- Name: IX_TeamDailyQuestionSpecifications_TeamDailyQuestionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyQuestionSpecifications_TeamDailyQuestionId" ON public."TeamDailyQuestionSpecifications" USING btree ("TeamDailyQuestionId");


--
-- Name: IX_TeamDailyQuestionSpecifications_TeamElement; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyQuestionSpecifications_TeamElement" ON public."TeamDailyQuestionSpecifications" USING btree ("TeamElement");


--
-- Name: IX_TeamDailyQuestions_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyQuestions_ContentCultureId" ON public."TeamDailyQuestions" USING btree ("ContentCultureId");


--
-- Name: IX_TeamDailyReasons_ContentCultureId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyReasons_ContentCultureId" ON public."TeamDailyReasons" USING btree ("ContentCultureId");


--
-- Name: IX_TeamDailyReasons_Specification; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyReasons_Specification" ON public."TeamDailyReasons" USING btree ("SpecificationId");


--
-- Name: IX_TeamDailyReasons_TeamState; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamDailyReasons_TeamState" ON public."TeamDailyReasons" USING btree ("TeamState");


--
-- Name: IX_TeamFeedback_BusinessUnitId_SubmissionDateTime; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamFeedback_BusinessUnitId_SubmissionDateTime" ON public."TeamFeedback" USING btree ("BusinessUnitId", "SubmissionDateTime");


--
-- Name: IX_TeamGoalAudits_Goal_LoggedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamGoalAudits_Goal_LoggedAt" ON public."TeamGoalAudits" USING btree ("TeamGoalId", "LoggedAt");


--
-- Name: IX_TeamGoalAudits_Operation; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamGoalAudits_Operation" ON public."TeamGoalAudits" USING btree ("OperationType");


--
-- Name: IX_TeamGoalAudits_Unit_LoggedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamGoalAudits_Unit_LoggedAt" ON public."TeamGoalAudits" USING btree ("UnitId", "LoggedAt");


--
-- Name: IX_TeamGoals_Profile_Unit; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamGoals_Profile_Unit" ON public."TeamGoals" USING btree ("ProfileId", "UnitId") WHERE ("IsDel" = false);


--
-- Name: IX_TeamQuestionCategories_IsActive; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamQuestionCategories_IsActive" ON public."TeamQuestionCategories" USING btree ("IsActive");


--
-- Name: IX_TeamSetups_Profile_Unit; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeamSetups_Profile_Unit" ON public."TeamSetups" USING btree ("ProfileId", "UnitId") WHERE ("IsDel" = false);


--
-- Name: IX_TimeAllocation_DailyReflectionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TimeAllocation_DailyReflectionId" ON public."TimeAllocation" USING btree ("DailyReflectionId");


--
-- Name: IX_UserRoles_UserGroupId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserRoles_UserGroupId" ON public."UserRoles" USING btree ("UserGroupId");


--
-- Name: IX_UserTeams_Pending_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserTeams_Pending_UserId" ON public."UserTeams" USING btree ("UserId") WHERE ("IsPendingActivation" = true);


--
-- Name: IX_UserTeams_UserId_IsUnAssigned; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserTeams_UserId_IsUnAssigned" ON public."UserTeams" USING btree ("UserId", "IsUnAssigned");


--
-- Name: IX_Users_UserGroupId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Users_UserGroupId" ON public."Users" USING btree ("UserGroupId");


--
-- Name: UX_PortfolioManagerAssignments_ManagerUserId_Active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UX_PortfolioManagerAssignments_ManagerUserId_Active" ON public."PortfolioManagerAssignments" USING btree ("ManagerUserId") WHERE ("IsUnAssigned" = false);


--
-- Name: UX_TeamDailyQuestionSpecifications_Category_SubCategory_Variant; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UX_TeamDailyQuestionSpecifications_Category_SubCategory_Variant" ON public."TeamDailyQuestionSpecifications" USING btree ("CategoryId", "SubCategory", "Variant", "Ordinal", "ContentCultureId");


--
-- Name: UX_TeamGoals_Unit_Order_Active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UX_TeamGoals_Unit_Order_Active" ON public."TeamGoals" USING btree ("UnitId", "DisplayOrder") WHERE (("IsActive" = true) AND ("IsDel" = false));


--
-- Name: UX_TeamQuestionCategories_Name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UX_TeamQuestionCategories_Name" ON public."TeamQuestionCategories" USING btree ("Name") WHERE ("IsDel" = false);


--
-- Name: UX_TeamSetups_Unit; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UX_TeamSetups_Unit" ON public."TeamSetups" USING btree ("UnitId") WHERE ("IsDel" = false);


--
-- Name: UX_UserRoles_UserId_UserGroupId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UX_UserRoles_UserId_UserGroupId" ON public."UserRoles" USING btree ("UserId", "UserGroupId");


--
-- Name: UX_Users_Username_Live; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UX_Users_Username_Live" ON public."Users" USING btree (lower("Username")) WHERE ("IsDel" = false);


--
-- Name: CompanySubscriptions CompanySubscriptions_PromoCodeId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanySubscriptions"
    ADD CONSTRAINT "CompanySubscriptions_PromoCodeId_fkey" FOREIGN KEY ("PromoCodeId") REFERENCES public."PromoCodes"("Id");


--
-- Name: DailyReflections DailyReflections_BusinessUnitId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyReflections"
    ADD CONSTRAINT "DailyReflections_BusinessUnitId_fkey" FOREIGN KEY ("BusinessUnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE CASCADE;


--
-- Name: ActionEvents FK_ActionEvents_ActionHubs_ActionHubId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ActionEvents"
    ADD CONSTRAINT "FK_ActionEvents_ActionHubs_ActionHubId" FOREIGN KEY ("ActionHubId") REFERENCES public."ActionHubs"("Id") ON DELETE CASCADE;


--
-- Name: AssignUserRights FK_AssignUserRights_PromptTypes_PromptTypeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AssignUserRights"
    ADD CONSTRAINT "FK_AssignUserRights_PromptTypes_PromptTypeId" FOREIGN KEY ("PromptTypeId") REFERENCES public."PromptTypes"("PromptTypeId") ON DELETE CASCADE;


--
-- Name: AssignUserRights FK_AssignUserRights_SystemModules_SystemModuleId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AssignUserRights"
    ADD CONSTRAINT "FK_AssignUserRights_SystemModules_SystemModuleId" FOREIGN KEY ("SystemModuleId") REFERENCES public."SystemModules"("ModuleID") ON DELETE CASCADE;


--
-- Name: AssignUserRights FK_AssignUserRights_UserGroups_UserGroupId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AssignUserRights"
    ADD CONSTRAINT "FK_AssignUserRights_UserGroups_UserGroupId" FOREIGN KEY ("UserGroupId") REFERENCES public."UserGroups"("Id") ON DELETE CASCADE;


--
-- Name: AssignedCompanyLicenses FK_AssignedCompanyLicenses_BusinessUnits_BusinessUnitId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AssignedCompanyLicenses"
    ADD CONSTRAINT "FK_AssignedCompanyLicenses_BusinessUnits_BusinessUnitId" FOREIGN KEY ("BusinessUnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE RESTRICT;


--
-- Name: AssignedCompanyLicenses FK_AssignedCompanyLicenses_CompanyLicenses_CompanyLicenseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AssignedCompanyLicenses"
    ADD CONSTRAINT "FK_AssignedCompanyLicenses_CompanyLicenses_CompanyLicenseId" FOREIGN KEY ("CompanyLicenseId") REFERENCES public."CompanyLicenses"("Id") ON DELETE CASCADE;


--
-- Name: BusinessUnits FK_BusinessUnits_DailyQuestionSetGroups_ActiveDailyQuestionSet~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BusinessUnits"
    ADD CONSTRAINT "FK_BusinessUnits_DailyQuestionSetGroups_ActiveDailyQuestionSet~" FOREIGN KEY ("ActiveDailyQuestionSetGroupId") REFERENCES public."DailyQuestionSetGroups"("Id") ON DELETE CASCADE;


--
-- Name: BusinessUnits FK_BusinessUnits_Profiles_ProfileId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BusinessUnits"
    ADD CONSTRAINT "FK_BusinessUnits_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES public."Profiles"("Id") ON DELETE CASCADE;


--
-- Name: CompanyLicenses FK_CompanyLicenses_CompanySubscriptions_CompanySubscriptionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanyLicenses"
    ADD CONSTRAINT "FK_CompanyLicenses_CompanySubscriptions_CompanySubscriptionId" FOREIGN KEY ("CompanySubscriptionId") REFERENCES public."CompanySubscriptions"("Id") ON DELETE SET NULL;


--
-- Name: CompanyLicenses FK_CompanyLicenses_Plans_PlanId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanyLicenses"
    ADD CONSTRAINT "FK_CompanyLicenses_Plans_PlanId" FOREIGN KEY ("PlanId") REFERENCES public."Plans"("Id") ON DELETE SET NULL;


--
-- Name: CompanyLicenses FK_CompanyLicenses_Profiles_ProfileId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanyLicenses"
    ADD CONSTRAINT "FK_CompanyLicenses_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES public."Profiles"("Id") ON DELETE RESTRICT;


--
-- Name: CompanySubscriptions FK_CompanySubscriptions_Plans; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanySubscriptions"
    ADD CONSTRAINT "FK_CompanySubscriptions_Plans" FOREIGN KEY ("PlanId") REFERENCES public."Plans"("Id") ON UPDATE RESTRICT ON DELETE RESTRICT;


--
-- Name: CompanySubscriptions FK_CompanySubscriptions_Profiles_ProfileId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CompanySubscriptions"
    ADD CONSTRAINT "FK_CompanySubscriptions_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES public."Profiles"("Id") ON UPDATE RESTRICT ON DELETE RESTRICT;


--
-- Name: ComplimentaryLicenseGrants FK_ComplimentaryLicenseGrants_BusinessUnits_ComplimentaryBusine; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ComplimentaryLicenseGrants"
    ADD CONSTRAINT "FK_ComplimentaryLicenseGrants_BusinessUnits_ComplimentaryBusine" FOREIGN KEY ("ComplimentaryBusinessUnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE RESTRICT;


--
-- Name: ComplimentaryLicenseGrants FK_ComplimentaryLicenseGrants_BusinessUnits_PrimaryBusinessUnit; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ComplimentaryLicenseGrants"
    ADD CONSTRAINT "FK_ComplimentaryLicenseGrants_BusinessUnits_PrimaryBusinessUnit" FOREIGN KEY ("PrimaryBusinessUnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE RESTRICT;


--
-- Name: ComplimentaryLicenseGrants FK_ComplimentaryLicenseGrants_CompanyLicenses_CompanyLicenseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ComplimentaryLicenseGrants"
    ADD CONSTRAINT "FK_ComplimentaryLicenseGrants_CompanyLicenses_CompanyLicenseId" FOREIGN KEY ("CompanyLicenseId") REFERENCES public."CompanyLicenses"("Id") ON DELETE CASCADE;


--
-- Name: CustomQuestionDailyFeedback FK_CustomQuestionDailyFeedback_BusinessUnits_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomQuestionDailyFeedback"
    ADD CONSTRAINT "FK_CustomQuestionDailyFeedback_BusinessUnits_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE RESTRICT;


--
-- Name: CustomQuestionDailyFeedback FK_CustomQuestionDailyFeedback_CustomQuestions_CustomQuestionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomQuestionDailyFeedback"
    ADD CONSTRAINT "FK_CustomQuestionDailyFeedback_CustomQuestions_CustomQuestionId" FOREIGN KEY ("CustomQuestionId") REFERENCES public."CustomQuestions"("Id") ON DELETE RESTRICT;


--
-- Name: CustomQuestions FK_CustomQuestions_BusinessUnits_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomQuestions"
    ADD CONSTRAINT "FK_CustomQuestions_BusinessUnits_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE RESTRICT;


--
-- Name: CustomQuestions FK_CustomQuestions_ContentCultures_ContentCultureId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomQuestions"
    ADD CONSTRAINT "FK_CustomQuestions_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE RESTRICT;


--
-- Name: CustomQuestions FK_CustomQuestions_Profiles_ProfileId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CustomQuestions"
    ADD CONSTRAINT "FK_CustomQuestions_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES public."Profiles"("Id") ON DELETE RESTRICT;


--
-- Name: DailyFeedbackReasons FK_DailyFeedbackReasons_DailyFeedback_DailyFeedbackId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyFeedbackReasons"
    ADD CONSTRAINT "FK_DailyFeedbackReasons_DailyFeedback_DailyFeedbackId" FOREIGN KEY ("DailyFeedbackId") REFERENCES public."DailyFeedback"("Id") ON DELETE CASCADE;


--
-- Name: DailyFeedbackReasons FK_DailyFeedbackReasons_DailyReasons_DailyReasonId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyFeedbackReasons"
    ADD CONSTRAINT "FK_DailyFeedbackReasons_DailyReasons_DailyReasonId" FOREIGN KEY ("DailyReasonId") REFERENCES public."DailyReasons"("Id") ON DELETE CASCADE;


--
-- Name: DailyFeedback FK_DailyFeedback_BusinessUnits_BusinessUnitId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyFeedback"
    ADD CONSTRAINT "FK_DailyFeedback_BusinessUnits_BusinessUnitId" FOREIGN KEY ("BusinessUnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE CASCADE;


--
-- Name: DailyFeedback FK_DailyFeedback_DailyQuestions_DailyQuestionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyFeedback"
    ADD CONSTRAINT "FK_DailyFeedback_DailyQuestions_DailyQuestionId" FOREIGN KEY ("DailyQuestionId") REFERENCES public."DailyQuestions"("Id") ON DELETE CASCADE;


--
-- Name: DailyFeedback FK_DailyFeedback_DailyReasons_DailyReasonId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyFeedback"
    ADD CONSTRAINT "FK_DailyFeedback_DailyReasons_DailyReasonId" FOREIGN KEY ("DailyReasonId") REFERENCES public."DailyReasons"("Id") ON DELETE CASCADE;


--
-- Name: DailyMicroActions FK_DailyMicroActions_ContentCultures_ContentCultureId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyMicroActions"
    ADD CONSTRAINT "FK_DailyMicroActions_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE CASCADE;


--
-- Name: DailyMicroActions FK_DailyMicroActions_DailyReasons_ReasonId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyMicroActions"
    ADD CONSTRAINT "FK_DailyMicroActions_DailyReasons_ReasonId" FOREIGN KEY ("ReasonId") REFERENCES public."DailyReasons"("Id") ON DELETE CASCADE;


--
-- Name: DailyQuestionSets FK_DailyQuestionSets_ContentCultures_ContentCultureId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestionSets"
    ADD CONSTRAINT "FK_DailyQuestionSets_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE CASCADE;


--
-- Name: DailyQuestionSets FK_DailyQuestionSets_DailyQuestionSetGroups_DailyQuestionSetGr~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestionSets"
    ADD CONSTRAINT "FK_DailyQuestionSets_DailyQuestionSetGroups_DailyQuestionSetGr~" FOREIGN KEY ("DailyQuestionSetGroupId") REFERENCES public."DailyQuestionSetGroups"("Id") ON DELETE CASCADE;


--
-- Name: DailyQuestionSpecifications FK_DailyQuestionSpecifications_ContentCultures_ContentCultureId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestionSpecifications"
    ADD CONSTRAINT "FK_DailyQuestionSpecifications_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE CASCADE;


--
-- Name: DailyQuestionSpecifications FK_DailyQuestionSpecifications_DailyQuestionSetGroups_DailyQue~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestionSpecifications"
    ADD CONSTRAINT "FK_DailyQuestionSpecifications_DailyQuestionSetGroups_DailyQue~" FOREIGN KEY ("DailyQuestionSetGroupId") REFERENCES public."DailyQuestionSetGroups"("Id") ON DELETE CASCADE;


--
-- Name: DailyQuestionSpecifications FK_DailyQuestionSpecifications_DailyQuestionSets_DailyQuestion~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestionSpecifications"
    ADD CONSTRAINT "FK_DailyQuestionSpecifications_DailyQuestionSets_DailyQuestion~" FOREIGN KEY ("DailyQuestionSetId") REFERENCES public."DailyQuestionSets"("Id") ON DELETE CASCADE;


--
-- Name: DailyQuestionSpecifications FK_DailyQuestionSpecifications_DailyQuestions_QuestionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestionSpecifications"
    ADD CONSTRAINT "FK_DailyQuestionSpecifications_DailyQuestions_QuestionId" FOREIGN KEY ("QuestionId") REFERENCES public."DailyQuestions"("Id") ON DELETE CASCADE;


--
-- Name: DailyQuestions FK_DailyQuestions_ContentCultures_ContentCultureId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyQuestions"
    ADD CONSTRAINT "FK_DailyQuestions_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE CASCADE;


--
-- Name: DailyReasons FK_DailyReasons_ContentCultures_ContentCultureId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyReasons"
    ADD CONSTRAINT "FK_DailyReasons_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE CASCADE;


--
-- Name: DailyReasons FK_DailyReasons_DailyQuestionSpecifications_SpecificationId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DailyReasons"
    ADD CONSTRAINT "FK_DailyReasons_DailyQuestionSpecifications_SpecificationId" FOREIGN KEY ("SpecificationId") REFERENCES public."DailyQuestionSpecifications"("Id") ON DELETE CASCADE;


--
-- Name: EranCoachMessages FK_EranCoachMessages_EranCoachConversations_ConversationId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."EranCoachMessages"
    ADD CONSTRAINT "FK_EranCoachMessages_EranCoachConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES public."EranCoachConversations"("Id") ON DELETE CASCADE;


--
-- Name: FeedBack FK_FeedBack_BusinessUnits; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FeedBack"
    ADD CONSTRAINT "FK_FeedBack_BusinessUnits" FOREIGN KEY ("BusinessUnitId") REFERENCES public."BusinessUnits"("Id");


--
-- Name: Payments FK_Payments_CompanySubscriptions; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Payments"
    ADD CONSTRAINT "FK_Payments_CompanySubscriptions" FOREIGN KEY ("CompanySubscriptionId") REFERENCES public."CompanySubscriptions"("Id") ON UPDATE RESTRICT ON DELETE RESTRICT;


--
-- Name: Payments FK_Payments_Profiles_ProfileId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Payments"
    ADD CONSTRAINT "FK_Payments_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES public."Profiles"("Id") ON UPDATE RESTRICT ON DELETE RESTRICT;


--
-- Name: PortfolioManagerAssignments FK_PortfolioManagerAssignments_Profiles_ProfileId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PortfolioManagerAssignments"
    ADD CONSTRAINT "FK_PortfolioManagerAssignments_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES public."Profiles"("Id") ON DELETE RESTRICT;


--
-- Name: PortfolioManagerAssignments FK_PortfolioManagerAssignments_Users_ManagerUserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PortfolioManagerAssignments"
    ADD CONSTRAINT "FK_PortfolioManagerAssignments_Users_ManagerUserId" FOREIGN KEY ("ManagerUserId") REFERENCES public."Users"("UserId") ON DELETE RESTRICT;


--
-- Name: PortfolioManagerAssignments FK_PortfolioManagerAssignments_Users_PortfolioManagerUserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PortfolioManagerAssignments"
    ADD CONSTRAINT "FK_PortfolioManagerAssignments_Users_PortfolioManagerUserId" FOREIGN KEY ("PortfolioManagerUserId") REFERENCES public."Users"("UserId") ON DELETE RESTRICT;


--
-- Name: Profiles FK_Profiles_Profiles_ParentId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Profiles"
    ADD CONSTRAINT "FK_Profiles_Profiles_ParentId" FOREIGN KEY ("ParentId") REFERENCES public."Profiles"("Id") ON DELETE RESTRICT;


--
-- Name: Profiles FK_Profiles_Profiles_PartnerId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Profiles"
    ADD CONSTRAINT "FK_Profiles_Profiles_PartnerId" FOREIGN KEY ("PartnerId") REFERENCES public."Profiles"("Id") ON DELETE RESTRICT;


--
-- Name: Subscriptions FK_Subscriptions_BusinessUnits_BusinessUnitId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subscriptions"
    ADD CONSTRAINT "FK_Subscriptions_BusinessUnits_BusinessUnitId" FOREIGN KEY ("BusinessUnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE CASCADE;


--
-- Name: Subscriptions FK_Subscriptions_Profiles_ProfileId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subscriptions"
    ADD CONSTRAINT "FK_Subscriptions_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES public."Profiles"("Id") ON DELETE CASCADE;


--
-- Name: TeamDailyFeedbackReasons FK_TeamDailyFeedbackReasons_TeamDailyFeedback_TeamDailyFeedbac~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyFeedbackReasons"
    ADD CONSTRAINT "FK_TeamDailyFeedbackReasons_TeamDailyFeedback_TeamDailyFeedbac~" FOREIGN KEY ("TeamDailyFeedbackId") REFERENCES public."TeamDailyFeedback"("Id") ON DELETE CASCADE;


--
-- Name: TeamDailyFeedbackReasons FK_TeamDailyFeedbackReasons_TeamDailyReasons_TeamDailyReasonId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyFeedbackReasons"
    ADD CONSTRAINT "FK_TeamDailyFeedbackReasons_TeamDailyReasons_TeamDailyReasonId" FOREIGN KEY ("TeamDailyReasonId") REFERENCES public."TeamDailyReasons"("Id") ON DELETE CASCADE;


--
-- Name: TeamDailyFeedback FK_TeamDailyFeedback_BusinessUnits_BusinessUnitId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyFeedback"
    ADD CONSTRAINT "FK_TeamDailyFeedback_BusinessUnits_BusinessUnitId" FOREIGN KEY ("BusinessUnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE RESTRICT;


--
-- Name: TeamDailyFeedback FK_TeamDailyFeedback_TeamDailyQuestions_TeamDailyQuestionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyFeedback"
    ADD CONSTRAINT "FK_TeamDailyFeedback_TeamDailyQuestions_TeamDailyQuestionId" FOREIGN KEY ("TeamDailyQuestionId") REFERENCES public."TeamDailyQuestions"("Id") ON DELETE RESTRICT;


--
-- Name: TeamDailyFeedback FK_TeamDailyFeedback_TeamDailyReasons_TeamDailyReasonId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyFeedback"
    ADD CONSTRAINT "FK_TeamDailyFeedback_TeamDailyReasons_TeamDailyReasonId" FOREIGN KEY ("TeamDailyReasonId") REFERENCES public."TeamDailyReasons"("Id") ON DELETE RESTRICT;


--
-- Name: TeamDailyMicroActions FK_TeamDailyMicroActions_ContentCultures_ContentCultureId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyMicroActions"
    ADD CONSTRAINT "FK_TeamDailyMicroActions_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE RESTRICT;


--
-- Name: TeamDailyMicroActions FK_TeamDailyMicroActions_TeamDailyReasons_TeamDailyReasonId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyMicroActions"
    ADD CONSTRAINT "FK_TeamDailyMicroActions_TeamDailyReasons_TeamDailyReasonId" FOREIGN KEY ("TeamDailyReasonId") REFERENCES public."TeamDailyReasons"("Id") ON DELETE CASCADE;


--
-- Name: TeamDailyQuestionSpecifications FK_TeamDailyQuestionSpecifications_ContentCultures_ContentCult~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyQuestionSpecifications"
    ADD CONSTRAINT "FK_TeamDailyQuestionSpecifications_ContentCultures_ContentCult~" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE RESTRICT;


--
-- Name: TeamDailyQuestionSpecifications FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_TeamDail~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyQuestionSpecifications"
    ADD CONSTRAINT "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_TeamDail~" FOREIGN KEY ("TeamDailyQuestionId") REFERENCES public."TeamDailyQuestions"("Id") ON DELETE RESTRICT;


--
-- Name: TeamDailyQuestionSpecifications FK_TeamDailyQuestionSpecifications_TeamQuestionCategories_Categ; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyQuestionSpecifications"
    ADD CONSTRAINT "FK_TeamDailyQuestionSpecifications_TeamQuestionCategories_Categ" FOREIGN KEY ("CategoryId") REFERENCES public."TeamQuestionCategories"("Id") ON DELETE RESTRICT;


--
-- Name: TeamDailyQuestions FK_TeamDailyQuestions_ContentCultures_ContentCultureId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyQuestions"
    ADD CONSTRAINT "FK_TeamDailyQuestions_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE RESTRICT;


--
-- Name: TeamDailyReasons FK_TeamDailyReasons_ContentCultures_ContentCultureId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyReasons"
    ADD CONSTRAINT "FK_TeamDailyReasons_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES public."ContentCultures"("Id") ON DELETE RESTRICT;


--
-- Name: TeamDailyReasons FK_TeamDailyReasons_TeamDailyQuestionSpecifications_Specificat~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamDailyReasons"
    ADD CONSTRAINT "FK_TeamDailyReasons_TeamDailyQuestionSpecifications_Specificat~" FOREIGN KEY ("SpecificationId") REFERENCES public."TeamDailyQuestionSpecifications"("Id") ON DELETE CASCADE;


--
-- Name: TeamFeedback FK_TeamFeedback_BusinessUnits_BusinessUnitId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamFeedback"
    ADD CONSTRAINT "FK_TeamFeedback_BusinessUnits_BusinessUnitId" FOREIGN KEY ("BusinessUnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE CASCADE;


--
-- Name: TeamGoals FK_TeamGoals_BusinessUnits_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamGoals"
    ADD CONSTRAINT "FK_TeamGoals_BusinessUnits_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE RESTRICT;


--
-- Name: TeamGoals FK_TeamGoals_Profiles_ProfileId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamGoals"
    ADD CONSTRAINT "FK_TeamGoals_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES public."Profiles"("Id") ON DELETE RESTRICT;


--
-- Name: TeamSetups FK_TeamSetups_BusinessUnits_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamSetups"
    ADD CONSTRAINT "FK_TeamSetups_BusinessUnits_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."BusinessUnits"("Id") ON DELETE RESTRICT;


--
-- Name: TeamSetups FK_TeamSetups_Profiles_ProfileId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeamSetups"
    ADD CONSTRAINT "FK_TeamSetups_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES public."Profiles"("Id") ON DELETE RESTRICT;


--
-- Name: TestCaseAssertions FK_TestCaseAssertions_TestCaseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TestCaseAssertions"
    ADD CONSTRAINT "FK_TestCaseAssertions_TestCaseId" FOREIGN KEY ("TestCaseId") REFERENCES public."TestCases"("Id");


--
-- Name: TimeAllocation FK_TimeEntryBand_DailyReflections_DailyReflectionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TimeAllocation"
    ADD CONSTRAINT "FK_TimeEntryBand_DailyReflections_DailyReflectionId" FOREIGN KEY ("DailyReflectionId") REFERENCES public."DailyReflections"("Id");


--
-- Name: UserRoles FK_UserRoles_UserGroups_UserGroupId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserRoles"
    ADD CONSTRAINT "FK_UserRoles_UserGroups_UserGroupId" FOREIGN KEY ("UserGroupId") REFERENCES public."UserGroups"("Id") ON DELETE RESTRICT;


--
-- Name: UserRoles FK_UserRoles_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserRoles"
    ADD CONSTRAINT "FK_UserRoles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("UserId") ON DELETE CASCADE;


--
-- Name: Users FK_Users_UserGroups_UserGroupId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "FK_Users_UserGroups_UserGroupId" FOREIGN KEY ("UserGroupId") REFERENCES public."UserGroups"("Id") ON DELETE RESTRICT;


--
-- Name: PromoCodeUsages PromoCodeUsages_CompanySubscriptionId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "PromoCodeUsages_CompanySubscriptionId_fkey" FOREIGN KEY ("CompanySubscriptionId") REFERENCES public."CompanySubscriptions"("Id");


--
-- Name: PromoCodeUsages PromoCodeUsages_PromoCodeId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "PromoCodeUsages_PromoCodeId_fkey" FOREIGN KEY ("PromoCodeId") REFERENCES public."PromoCodes"("Id") ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

