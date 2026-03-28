import React, { useState, useEffect } from 'react';
import { Calendar as CalendarIcon, MessageCircle, AlertCircle } from 'lucide-react';
import axios from 'axios';
import { format, isSameDay, parseISO } from 'date-fns';

const StatsBar = () => {
  const [stats, setStats] = useState({
    todayBookings: 0,
    activeSessions: 0,
    needsAttention: 0
  });

  const fetchStats = async () => {
    try {
      const [bookingsRes, inboxRes] = await Promise.all([
        axios.get('/api/dashboard/bookings'),
        axios.get('/api/dashboard/sessions')
      ]);

      const today = new Date();
      const todayBookingsCount = (bookingsRes.data || []).filter(b => {
        try {
          const bDate = b.scheduledDate ? parseISO(b.scheduledDate) : null;
          return bDate && isSameDay(bDate, today);
        } catch (e) { return false; }
      }).length;

      const activeList = inboxRes.data || [];
      const needsAttentionCount = activeList.filter(s => s.needsHumanAttention).length;

      setStats({
        todayBookings: todayBookingsCount,
        activeSessions: activeList.length,
        needsAttention: needsAttentionCount
      });
    } catch (error) {
      console.error('Error fetching stats:', error);
    }
  };

  useEffect(() => {
    fetchStats();

    // Setup SignalR connection for Live Updates
    const setupSignalR = async () => {
      if (window.appSignalRConnection) {
        window.appSignalRConnection.on("ReceiveSessionUpdate", fetchStats);
      }
    };
    
    // Slight delay to ensure connection is established by DashboardLayout
    setTimeout(setupSignalR, 1000);

    return () => {
      if (window.appSignalRConnection) {
        window.appSignalRConnection.off("ReceiveSessionUpdate", fetchStats);
      }
    };
  }, []);

  return (
    <div className="flex items-center gap-4 text-xs font-semibold ml-4">
      {/* Citas de Hoy */}
      <div className="flex items-center gap-2 bg-white px-3 py-1.5 rounded-xl border border-slate-200/60 shadow-sm text-slate-600">
        <CalendarIcon size={14} className="text-blue-500" />
        <span>Citas Hoy: <strong className="text-slate-900 ml-1">{stats.todayBookings}</strong></span>
      </div>

      {/* Activas */}
      <div className="flex items-center gap-2 bg-white px-3 py-1.5 rounded-xl border border-slate-200/60 shadow-sm text-slate-600">
        <MessageCircle size={14} className="text-green-500" />
        <span>Chats Activos: <strong className="text-slate-900 ml-1">{stats.activeSessions}</strong></span>
      </div>

      {/* Requiere Atención (Solo aparece si > 0) */}
      {stats.needsAttention > 0 && (
        <div className="flex items-center gap-2 bg-red-50 px-3 py-1.5 rounded-xl border border-red-200 shadow-sm text-red-700 animate-pulse">
          <AlertCircle size={14} className="text-red-500" />
          <span>¡Atención Requerida!: <strong className="font-extrabold ml-1">{stats.needsAttention}</strong></span>
        </div>
      )}
    </div>
  );
};

export default StatsBar;
