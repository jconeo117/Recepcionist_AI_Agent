import React, { useState, useEffect } from 'react';
import { NavLink } from 'react-router-dom';
import axios from 'axios';
import { 
  Inbox, 
  Calendar, 
  Users, 
  Database, 
  Settings, 
  LogOut 
} from 'lucide-react';
import { useAuth } from '../context/AuthContext';

const Sidebar = () => {
  const { logout, businessName } = useAuth();
  const [needsAttention, setNeedsAttention] = useState(0);

  useEffect(() => {
    const fetchStats = async () => {
      try {
        const res = await axios.get('/api/dashboard/stats');
        setNeedsAttention(res.data.needsAttention || 0);
      } catch (err) {
        // Silently fail for sidebar stats 
      }
    };
    fetchStats();
    
    // Refresh stats every 30 seconds for the badge
    const interval = setInterval(fetchStats, 30000);
    return () => clearInterval(interval);
  }, []);

  const navItems = [
    { name: 'Inbox', path: '/inbox', icon: Inbox, badge: needsAttention },
    { name: 'Reservas', path: '/bookings', icon: Calendar },
    { name: 'Proveedores', path: '/providers', icon: Users },
    { name: 'Base de datos', path: '/database', icon: Database },
  ];

  return (
    <aside id="sidebar" className="w-[var(--sidebar-w)] bg-[var(--surface)] border-r border-[var(--border)] flex flex-col shrink-0 py-5 transition-transform duration-250 ease-in-out">
      <div className="font-display text-lg font-bold tracking-tight px-5 pb-6 flex items-center gap-2">
        <span className="w-2 h-2 bg-[var(--accent)] rounded-full"></span>
        Dashboard
      </div>

      <div className="px-3 mb-1">
        <div className="text-[10px] font-semibold tracking-widest uppercase text-[var(--text-muted)] px-2 my-3">Principal</div>
        {navItems.map((item) => (
          <NavLink
            key={item.name}
            to={item.path}
            className={({ isActive }) => `
              flex items-center gap-2.5 px-2.5 py-2 rounded-lg cursor-pointer transition-all duration-[var(--transition)] text-[13.5px] mb-1
              ${isActive 
                ? 'bg-[var(--accent-light)] text-[var(--accent)] font-medium' 
                : 'text-[var(--text-secondary)] hover:bg-[var(--bg)] hover:text-[var(--text-primary)]'}
            `}
          >
            <item.icon size={18} className="shrink-0 opacity-70" />
            {item.name}
            {item.badge && (
              <span className="ml-auto bg-[var(--accent)] text-white text-[10px] font-semibold px-1.5 py-0.5 rounded-full min-w-[18px] text-center">
                {item.badge}
              </span>
            )}
          </NavLink>
        ))}
      </div>

      <div className="px-3">
        <div className="text-[10px] font-semibold tracking-widest uppercase text-[var(--text-muted)] px-2 my-3">Config</div>
        <NavLink
          to="/settings"
          className={({ isActive }) => `
            flex items-center gap-2.5 px-2.5 py-2 rounded-lg cursor-pointer transition-all duration-[var(--transition)] text-[13.5px]
            ${isActive 
              ? 'bg-[var(--accent-light)] text-[var(--accent)] font-medium' 
              : 'text-[var(--text-secondary)] hover:bg-[var(--bg)] hover:text-[var(--text-primary)]'}
          `}
        >
          <Settings size={18} className="shrink-0 opacity-70" />
          Ajustes
        </NavLink>
      </div>

      <div className="mt-auto px-3 border-t border-[var(--border)] pt-4">
        <div className="flex items-center gap-2.5 p-2 rounded-lg hover:bg-[var(--bg)] transition-colors cursor-pointer group">
          <div className="w-8 h-8 rounded-full bg-gradient-to-br from-[var(--accent)] to-[#7C3AED] flex items-center justify-center text-white text-xs font-semibold shrink-0">
            {businessName?.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2) || 'AD'}
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[13px] font-medium truncate">{businessName || 'Administrador'}</div>
            <div className="text-[11px] text-[var(--text-muted)]">Panel de Control</div>
          </div>
          <button 
            onClick={logout}
            className="text-[var(--text-muted)] hover:text-red-500 transition-colors"
            title="Cerrar Sesión"
          >
            <LogOut size={14} />
          </button>
        </div>
      </div>
    </aside>
  );
};

export default Sidebar;
