namespace SonosMonitoring.Infrastructure.MediaControl;

/// <summary>Provides explicit first installation of the execution coordination store, never a reset.</summary>
public static class MediaCoordinationSchema
{
    /// <summary>Creates version 1 atomically and fails when a schema already exists.</summary>
    /// <returns>PostgreSQL initialization SQL.</returns>
    public static string GetPostgresScript() => """
        BEGIN;
        CREATE SCHEMA sonos_media;
        CREATE TABLE sonos_media.version (singleton boolean PRIMARY KEY CHECK(singleton), version integer NOT NULL);
        INSERT INTO sonos_media.version VALUES(true,1);
        CREATE TABLE sonos_media.control (
            singleton boolean PRIMARY KEY CHECK(singleton), enabled boolean NOT NULL,
            generation uuid NOT NULL, restored_at timestamptz);
        INSERT INTO sonos_media.control VALUES(true,false,gen_random_uuid(),NULL);
        CREATE TABLE sonos_media.target_epochs (device_key varchar(128) PRIMARY KEY, epoch bigint NOT NULL CHECK(epoch>0));
        CREATE TABLE sonos_media.operations (
            id uuid PRIMARY KEY, position bigserial UNIQUE NOT NULL,
            fingerprint varchar(64) NOT NULL, generation uuid NOT NULL,
            targets text[] NOT NULL CHECK(cardinality(targets) BETWEEN 1 AND 256),
            payload jsonb NOT NULL, accepted_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
            status varchar(40) NOT NULL DEFAULT 'accepted', step integer NOT NULL DEFAULT -1,
            code varchar(100), risk_released boolean NOT NULL DEFAULT false,
            outcome_recorded boolean NOT NULL DEFAULT false);
        CREATE INDEX media_operations_pending ON sonos_media.operations(position) WHERE status IN ('accepted','dispatch_started','device_acknowledged','outcome_unknown');
        CREATE INDEX media_operation_targets ON sonos_media.operations USING gin(targets);
        CREATE TABLE sonos_media.steps (
            operation uuid NOT NULL REFERENCES sonos_media.operations(id), step integer NOT NULL,
            released_at timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(operation,step));
        CREATE TABLE sonos_media.decisions (
            id uuid PRIMARY KEY, fingerprint varchar(300) NOT NULL, decided_at timestamptz NOT NULL DEFAULT clock_timestamp());
        COMMIT;
        """;
}
