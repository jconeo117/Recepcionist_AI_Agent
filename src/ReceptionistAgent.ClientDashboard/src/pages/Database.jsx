import React, { useState, useEffect } from 'react';
import axios from 'axios';
import { format } from 'date-fns';
import { es } from 'date-fns/locale';
import DashboardLayout from '../components/DashboardLayout';
import Topbar from '../components/Topbar';
import { Database as DbIcon, Code, Play } from 'lucide-react';

const Database = () => {
  const [activeTable, setActiveTable] = useState('bookings');
  const [data, setData] = useState([]);
  const [loading, setLoading] = useState(true);
  const tables = ['bookings', 'providers', 'clients', 'messages', 'chat_messages', 'services', 'invoices'];

  useEffect(() => {
    fetchTableData(activeTable);
  }, [activeTable]);

  const fetchTableData = async (tableName) => {
    try {
      setLoading(true);
      const res = await axios.get(`/api/dashboard/database/${tableName}`);
      setData(res.data || []);
    } catch (err) {
      console.error('Error fetching table data', err);
      setData([]);
    } finally {
      setLoading(false);
    }
  };

  const renderValue = (val) => {
    if (val === null || val === undefined) return <span className="text-slate-300">null</span>;
    if (typeof val === 'boolean') return val ? 'true' : 'false';
    if (typeof val === 'object') return '{...}';
    return String(val);
  };

  return (
    <DashboardLayout>
      <Topbar title="Base de Datos" actionLabel="Ejecutar" />
      <div className="flex-1 overflow-hidden flex p-7 gap-5">
        <div className="w-[200px] shrink-0 border border-[var(--border)] bg-[var(--surface)] rounded-[var(--radius)] overflow-hidden flex flex-col">
          <div className="px-3.5 py-3 text-[11px] font-semibold tracking-widest uppercase text-[var(--text-muted)] border-bottom border-[var(--border)]">Tablas</div>
          <div className="flex-1 overflow-y-auto">
            {tables.map((t) => (
              <div 
                key={t} 
                onClick={() => setActiveTable(t)}
                className={`px-3.5 py-2.5 text-[13px] border-b border-[var(--border)] last:border-none flex items-center gap-2 cursor-pointer transition-all duration-[var(--transition)] hover:bg-[var(--bg)] ${activeTable === t ? 'bg-[var(--accent-light)] text-[var(--accent)] font-medium' : 'text-[var(--text-primary)]'}`}
              >
                <DbIcon size={13} className="opacity-50" />
                {t}
              </div>
            ))}
          </div>
        </div>

        <div className="flex-1 min-w-0 flex flex-col gap-4">
          <div className="border border-[var(--border)] bg-[var(--surface)] rounded-[var(--radius)] overflow-hidden">
            <div className="px-4 py-2.5 bg-[var(--bg)] border-b border-[var(--border)] flex items-center gap-2">
              <Code size={13} className="text-[var(--accent)]" />
              <div className="flex-1 text-[11px] font-semibold tracking-widest uppercase text-[var(--text-muted)]">Query Editor</div>
              <button className="px-3 py-1 bg-[var(--accent)] text-white border-none rounded-[var(--radius-sm)] text-[12px] flex items-center gap-1.5 cursor-pointer">
                <Play size={11} fill="currentColor" />
                Ejecutar
              </button>
            </div>
            <textarea 
              readOnly
              className="w-full px-4 py-3.5 h-[100px] font-mono text-[13px] text-[var(--accent)] bg-[#FAFAF8] border-none outline-none resize-none leading-relaxed"
              value={`SELECT * FROM ${activeTable} ORDER BY created_at DESC LIMIT 50;`}
            />
          </div>

          <div className="flex-1 border border-[var(--border)] bg-[var(--surface)] rounded-[var(--radius)] overflow-hidden flex flex-col">
            <div className="px-5 py-4 border-b border-[var(--border)] flex items-center justify-between">
              <div className="font-display text-[14px] font-bold">{activeTable} <span className="font-body text-[12px] text-[var(--text-muted)] font-normal ml-1">— {data.length}{data.length === 50 ? '+' : ''} registros</span></div>
              <div className="flex items-center gap-1 text-[var(--green)] text-[12px]">
                <span className="w-1.5 h-1.5 bg-[var(--green)] rounded-full"></span>
                Conectado
              </div>
            </div>
            <div className="flex-1 overflow-auto">
              {loading ? (
                <div className="h-full flex items-center justify-center p-10">
                  <div className="animate-spin rounded-full h-8 w-8 border-4 border-[var(--accent)] border-t-transparent"></div>
                </div>
              ) : data.length === 0 ? (
                <div className="p-10 text-center text-[13px] text-slate-400 font-medium italic">Esta tabla no tiene registros disponibles en este momento.</div>
              ) : (
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="bg-[var(--bg)] sticky top-0 z-10">
                      {Object.keys(data[0] || {}).slice(0, 6).map((h) => (
                        <th key={h} className="px-5 py-2.5 text-left text-[11px] font-semibold tracking-widest uppercase text-[var(--text-muted)] border-b border-[var(--border)] bg-[var(--bg)]">{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {data.map((row, i) => (
                      <tr key={i} className="hover:bg-[var(--bg)] transition-colors">
                        {Object.values(row).slice(0, 6).map((val, j) => (
                          <td key={j} className="px-5 py-3 text-[13px] border-b border-[var(--border)] truncate max-w-[200px]">
                            {renderValue(val)}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        </div>
      </div>
    </DashboardLayout>
  );
};

export default Database;
