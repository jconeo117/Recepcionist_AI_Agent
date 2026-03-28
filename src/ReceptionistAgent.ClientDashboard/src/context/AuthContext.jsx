import React, { createContext, useContext, useState, useEffect } from 'react';
import axios from 'axios';

const AuthContext = createContext();

export const useAuth = () => useContext(AuthContext);

export const AuthProvider = ({ children }) => {
  const [tenant, setTenant] = useState(null);
  const [isLoading, setIsLoading] = useState(true);

  // Configure axios defaults for cookies
  axios.defaults.withCredentials = true;

  useEffect(() => {
    // Check if session is already active via httpOnly cookie
    const checkSession = async () => {
      try {
        const response = await axios.get('/api/dashboard/settings');
        if (response.data) {
          setTenant({
            id: response.data.tenantId,
            name: response.data.businessName || 'Admin',
          });
        }
      } catch (e) {
        // Not logged in or expired
        setTenant(null);
      } finally {
        setIsLoading(false);
      }
    };
    
    checkSession();
  }, []);

  const login = async (username, password) => {
    try {
      const response = await axios.post('/api/tenant/auth/login', { username, password });
      
      // Token is in httpOnly cookie, not accessible here
      setTenant({
        id: response.data.tenantId,
        name: response.data.businessName || 'Admin',
      });
      
      return { success: true };
    } catch (error) {
      console.error('Login attempt failed.');
      
      let errorMsg = 'Error al iniciar sesión. Verifique sus credenciales.';
      if (error.response?.data) {
          if (typeof error.response.data === 'string') {
              errorMsg = error.response.data;
          } else if (error.response.data.error) {
              errorMsg = error.response.data.error;
          } else if (error.response.data.title) {
              errorMsg = error.response.data.title;
          }
      }

      return { 
        success: false, 
        error: errorMsg
      };
    }
  };

  const logout = async () => {
    try {
      await axios.post('/api/tenant/auth/logout');
    } catch (e) {
      console.error('Logout request failed');
    } finally {
      setTenant(null);
    }
  };

  return (
    <AuthContext.Provider value={{ tenant, login, logout, isLoading }}>
      {!isLoading && children}
    </AuthContext.Provider>
  );
};
