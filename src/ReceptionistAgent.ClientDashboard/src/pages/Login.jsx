import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { LogIn, Building, AlertCircle } from 'lucide-react';

const Login = () => {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      const result = await login(username, password);
      // login via our provider sets the token in local storage and axios headers
      if (result.success) {
        navigate('/inbox');
      } else {
        setError(result.error);
      }
    } catch (err) {
      setError('Ocurrió un error inesperado. Intente de nuevo.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-100 dark:bg-slate-950 p-4">
      <div className="w-full max-w-md">
        
        {/* Banner Logo */}
        <div className="flex flex-col items-center mb-8">
          <div className="w-16 h-16 bg-blue-600 rounded-xl shadow-lg flex items-center justify-center mb-4 transform hover:scale-105 transition-transform duration-300">
            <Building className="w-8 h-8 text-white" />
          </div>
          <h1 className="text-3xl font-bold bg-clip-text text-transparent bg-gradient-to-r from-blue-600 to-indigo-500">
            Client Dashboard
          </h1>
          <p className="text-slate-500 dark:text-slate-400 mt-2 text-center text-sm">
            Gestione las conversaciones con sus clientes y atienda las alertas del Asistente Virtual.
          </p>
        </div>

        {/* Login Form */}
        <div className="bg-white dark:bg-slate-900 rounded-2xl shadow-xl overflow-hidden border border-slate-200 dark:border-slate-800 transition-colors">
          <div className="p-8">
            <h2 className="text-xl font-semibold mb-6 flex items-center gap-2">
              <LogIn className="w-5 h-5 text-blue-500" />
              Inicie sesión
            </h2>

            {error && (
              <div className="bg-red-50 text-red-600 dark:bg-red-900/30 dark:text-red-400 p-4 rounded-xl text-sm mb-6 flex items-start gap-3 animate-pulse border border-red-200 dark:border-red-900/50">
                <AlertCircle className="w-5 h-5 shrink-0 mt-0.5" />
                <p>{error}</p>
              </div>
            )}

            <form onSubmit={handleSubmit} className="space-y-5">
              <div>
                <label className="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-2">
                  Usuario
                </label>
                <input
                  type="text"
                  required
                  placeholder="ID del Tenant o Usuario"
                  className="input-field"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-2">
                  Contraseña
                </label>
                <input
                  type="password"
                  required
                  placeholder="••••••••"
                  className="input-field"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </div>

              <div className="pt-4">
                <button
                  type="submit"
                  disabled={loading}
                  className="w-full btn-primary py-3 relative overflow-hidden group disabled:opacity-70"
                >
                  <span className={`flex items-center justify-center gap-2 transition-transform duration-300 ${loading ? 'scale-90 opacity-0' : 'scale-100 opacity-100'}`}>
                    Ingresar
                  </span>
                  
                  {loading && (
                    <div className="absolute inset-0 flex items-center justify-center">
                      <div className="w-5 h-5 border-2 border-white/50 border-t-white rounded-full animate-spin" />
                    </div>
                  )}
                  
                  {/* Interaction ripple hint */}
                  <div className="absolute inset-0 bg-white/10 translate-y-full group-hover:translate-y-0 transition-transform duration-300 pointer-events-none" />
                </button>
              </div>
            </form>
          </div>
        </div>

        {/* Footer info */}
        <p className="text-center text-xs text-slate-400 mt-8">
          © {new Date().getFullYear()} Receptionist Agent AI. Todos los derechos reservados.
        </p>

      </div>
    </div>
  );
};

export default Login;
