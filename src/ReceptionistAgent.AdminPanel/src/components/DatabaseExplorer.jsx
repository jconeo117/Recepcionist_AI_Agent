import { useState, useEffect, useCallback } from "react";
import { useTheme, fmt, fmtTime } from "../styles/tokens";
import { LoadingState, ErrorBanner } from "./Feedback";
import { fetchDatabaseSchema, fetchDatabaseBookings } from "../api/client";

export default function DatabaseExplorer({ tenantId, dbType }) {
  const { colors: C, styles: s } = useTheme();
  const [tab, setTab] = useState("schema");
  const [schema, setSchema] = useState(null);
  const [bookings, setBookings] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const loadSchema = useCallback(async () => {
    setLoading(true); setError(null);
    try { setSchema(await fetchDatabaseSchema(tenantId)); }
    catch (err) { setError(err.response?.data?.error || err.message || "Error al cargar esquema"); }
    finally { setLoading(false); }
  }, [tenantId]);

  const loadBookings = useCallback(async () => {
    setLoading(true); setError(null);
    try { setBookings(await fetchDatabaseBookings(tenantId, 50)); }
    catch (err) { setError(err.response?.data?.error || err.message || "Error al cargar bookings"); }
    finally { setLoading(false); }
  }, [tenantId]);

  useEffect(() => {
    if (tab === "schema" && !schema) loadSchema();
    if (tab === "bookings" && !bookings) loadBookings();
  }, [tab, schema, bookings, loadSchema, loadBookings]);

  if (dbType === "InMemory") {
    return (
      <div style={{ padding: "20px 16px", borderRadius: 6, background: C.surfaceHover, border: `1px solid ${C.border}`, fontSize: 12, color: C.textMuted, textAlign: "center" }}>
        ⓘ Este tenant usa <strong style={{ color: C.text }}>InMemory</strong>.
        <br />La exploración de DB solo está disponible para tenants con bases de datos persistentes.
      </div>
    );
  }

  return (
    <div className="fade-in">
      <div style={{ display: "flex", gap: 4, marginBottom: 16 }}>
        {[{ id: "schema", label: "Esquema" }, { id: "bookings", label: "Bookings" }].map(t => (
          <button key={t.id} style={{
            ...s.btn(), fontSize: 10,
            background: tab === t.id ? C.accentDim : C.surfaceHover,
            color: tab === t.id ? C.accent : C.textMuted,
            border: `1px solid ${tab === t.id ? C.accentBorder : "transparent"}`,
          }} onClick={() => setTab(t.id)}>{t.label}</button>
        ))}
      </div>

      {loading && <LoadingState label={tab === "schema" ? "Cargando esquema..." : "Cargando bookings..."} />}
      {error && <ErrorBanner message={error} onRetry={tab === "schema" ? loadSchema : loadBookings} />}

      {!loading && !error && tab === "schema" && schema && (
        <div>
          <div style={{ marginBottom: 14 }}>
            <div style={{ fontSize: 10, color: C.textDim, letterSpacing: "0.08em", textTransform: "uppercase", marginBottom: 8 }}>
              Tablas ({schema.tables?.length || 0})
            </div>
            <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
              {(schema.tables || []).map(t => (
                <span key={t.tableName} style={{
                  ...s.tag,
                  color: t.tableName === "Bookings" ? C.accent : C.textMuted,
                  background: t.tableName === "Bookings" ? C.accentDim : C.surfaceHover,
                  border: t.tableName === "Bookings" ? `1px solid ${C.accentBorder}` : "none",
                }}>{t.tableName}</span>
              ))}
            </div>
          </div>
          <div style={{ fontSize: 10, color: C.textDim, letterSpacing: "0.08em", textTransform: "uppercase", marginBottom: 8 }}>
            Columnas de Bookings ({schema.bookingsSchema?.length || 0})
          </div>
          {schema.bookingsSchema?.length > 0 ? (
            <table style={s.table}>
              <thead><tr>{["#", "Columna", "Tipo", "Nullable", "Max Length", "Default"].map(h => <th key={h} style={s.th}>{h}</th>)}</tr></thead>
              <tbody>
                {schema.bookingsSchema.map(col => (
                  <tr key={col.columnName}>
                    <td style={{ ...s.td, fontSize: 10, color: C.textDim }}>{col.ordinalPosition}</td>
                    <td style={{ ...s.td, fontWeight: 500, fontFamily: "monospace", fontSize: 11 }}>{col.columnName}</td>
                    <td style={s.td}><span style={s.chip(C.blue)}>{col.dataType}</span></td>
                    <td style={s.td}><span style={{ fontSize: 10, color: col.isNullable === "YES" ? C.textMuted : C.accent }}>{col.isNullable === "YES" ? "SI" : "NO"}</span></td>
                    <td style={{ ...s.td, fontSize: 11, color: C.textMuted }}>{col.maxLength || "—"}</td>
                    <td style={{ ...s.td, fontSize: 10, color: C.textDim, fontFamily: "monospace" }}>{col.defaultValue || "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <div style={{ fontSize: 12, color: C.textDim, padding: "12px 0" }}>La tabla Bookings no existe.</div>
          )}
        </div>
      )}

      {!loading && !error && tab === "bookings" && bookings && (
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12 }}>
            <div style={{ fontSize: 10, color: C.textDim, letterSpacing: "0.08em", textTransform: "uppercase" }}>
              Mostrando {bookings.showing} de {bookings.totalBookings}
            </div>
            <button style={s.btn()} onClick={() => { setBookings(null); loadBookings(); }}>↻ Refrescar</button>
          </div>
          {bookings.bookings?.length > 0 ? (
            <div style={{ overflowX: "auto" }}>
              <table style={s.table}>
                <thead><tr>{["Código", "Cliente", "Provider", "Fecha", "Hora", "Estado", "Creado"].map(h => <th key={h} style={s.th}>{h}</th>)}</tr></thead>
                <tbody>
                  {bookings.bookings.map((b, idx) => (
                    <tr key={b.Id || b.id || idx}>
                      <td style={{ ...s.td, fontFamily: "monospace", fontSize: 11, color: C.accent }}>{b.ConfirmationCode || b.confirmationCode || "—"}</td>
                      <td style={{ ...s.td, fontSize: 12 }}>{b.ClientName || b.clientName || "—"}</td>
                      <td style={{ ...s.td, fontSize: 11, color: C.textMuted }}>{b.ProviderName || b.providerName || "—"}</td>
                      <td style={{ ...s.td, fontSize: 11, color: C.textMuted }}>{(b.ScheduledDate || b.scheduledDate) ? fmt(b.ScheduledDate || b.scheduledDate) : "—"}</td>
                      <td style={{ ...s.td, fontSize: 11, color: C.textDim }}>{b.ScheduledTime || b.scheduledTime || "—"}</td>
                      <td style={s.td}>
                        <span style={s.chip(
                          (b.Status || b.status) === "Scheduled" ? C.blue :
                          (b.Status || b.status) === "Confirmed" ? C.green :
                          (b.Status || b.status) === "Cancelled" ? C.red : C.textMuted
                        )}>{b.Status || b.status || "—"}</span>
                      </td>
                      <td style={{ ...s.td, fontSize: 10, color: C.textDim }}>{(b.CreatedAt || b.createdAt) ? fmt(b.CreatedAt || b.createdAt) : "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div style={{ fontSize: 12, color: C.textDim, padding: "20px 0", textAlign: "center" }}>Sin bookings.</div>
          )}
        </div>
      )}
    </div>
  );
}
