import React, { useState, useEffect } from 'react';
import { useAuth } from '../context/AuthContext';
import DashboardLayout from '../components/DashboardLayout';
import Topbar from '../components/Topbar';
import axios from 'axios';

const Settings = () => {
  const { tenant } = useAuth();
  const [settings, setSettings] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchSettings = async () => {
      try {
        const res = await axios.get('/api/dashboard/settings');
        setSettings(res.data);
      } catch (err) {
        console.error('Error fetching settings', err);
      } finally {
        setLoading(false);
      }
    };
    fetchSettings();
  }, []);

  const businessName = settings?.businessName || tenant?.name || 'Cargando...';
  const tenantId = settings?.tenantId || tenant?.id || '-';
  
  const getInitials = (name) => {
    return name?.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2) || 'TN';
  };

  if (loading && !settings) {
    return (
      <DashboardLayout>
        <Topbar title="Ajustes" showAction={false} />
        <div className="flex-1 flex items-center justify-center">
          <div className="animate-spin rounded-full h-8 w-8 border-4 border-[var(--accent)] border-t-transparent"></div>
        </div>
      </DashboardLayout>
    );
  }

  return (
    <DashboardLayout>
      <Topbar title="Ajustes" showAction={false} />
      <div className="flex-1 overflow-y-auto p-7">
        <div className="max-w-[560px]">
          <div className="font-display text-[15px] font-bold tracking-tight mb-3.5 px-1">Perfil de Empresa</div>
          
          <div className="bg-[var(--surface)] border border-[var(--border)] rounded-[var(--radius)] p-5 mb-5">
            <div className="flex items-center gap-5 mb-5 px-1">
              <div className="w-[52px] h-[52px] rounded-xl bg-gradient-to-br from-[var(--accent)] to-[#7C3AED] flex items-center justify-center text-white text-[18px] font-bold">
                {getInitials(businessName)}
              </div>
              <div>
                <div className="font-bold">{businessName}</div>
                <div className="text-[13px] text-[var(--text-secondary)]">ID: {tenantId}</div>
              </div>
            </div>

            <div className="mb-4">
              <label className="block text-[12px] font-semibold text-[var(--text-secondary)] mb-1.5 uppercase tracking-widest px-1">Nombre del Negocio</label>
              <input type="text" className="input-field" value={businessName} readOnly />
            </div>
            <div className="mb-4">
              <label className="block text-[12px] font-semibold text-[var(--text-secondary)] mb-1.5 uppercase tracking-widest px-1">Tipo de Negocio</label>
              <input type="text" className="input-field" value={settings?.businessType || '-'} readOnly />
            </div>
            <div className="mb-4">
              <label className="block text-[12px] font-semibold text-[var(--text-secondary)] mb-1.5 uppercase tracking-widest px-1">Dirección</label>
              <input type="text" className="input-field" value={settings?.address || '-'} readOnly />
            </div>
            
            <p className="text-[11px] text-[var(--text-muted)] mt-4 px-1 italic">
              * Para modificar datos maestros, por favor contacte con soporte técnico.
            </p>
          </div>

          <div className="font-display text-[15px] font-bold tracking-tight mb-3.5 px-1">Configuración Regional</div>
          <div className="bg-[var(--surface)] border border-[var(--border)] rounded-[var(--radius)] p-5">
            <div className="mb-4">
              <label className="block text-[12px] font-semibold text-[var(--text-secondary)] mb-1.5 uppercase tracking-widest px-1">Zona horaria</label>
              <input type="text" className="input-field" value={settings?.timeZoneId || 'America/Bogota'} readOnly />
            </div>
            <div>
              <label className="block text-[12px] font-semibold text-[var(--text-secondary)] mb-1.5 uppercase tracking-widest px-1">Modalidad de Servicio</label>
              <input type="text" className="input-field" value={settings?.serviceModality || 'Presencial'} readOnly />
            </div>
          </div>
        </div>
      </div>
    </DashboardLayout>
  );
};

export default Settings;
