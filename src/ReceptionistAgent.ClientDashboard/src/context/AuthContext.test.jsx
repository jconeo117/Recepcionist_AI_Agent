import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import axios from 'axios';
import React from 'react';
import { AuthProvider, useAuth } from './AuthContext';

// Mock axios methods
vi.mock('axios');

// Helper component to consume AuthContext
const TestComponent = () => {
  const { tenant, isLoading, login, logout } = useAuth();

  if (isLoading) return <div data-testid="loading">Loading...</div>;

  return (
    <div>
      <div data-testid="tenant-id">{tenant ? tenant.id : 'No Tenant'}</div>
      <button onClick={() => login('user', 'pass')} data-testid="login-btn">Login</button>
      <button onClick={logout} data-testid="logout-btn">Logout</button>
    </div>
  );
};

describe('AuthContext', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('initially checks session and sets tenant if successful', async () => {
    // Mock the initial GET /api/dashboard/settings
    axios.get.mockResolvedValueOnce({
      data: { tenantId: 'tenant-123', businessName: 'Test Business' }
    });

    render(
      <AuthProvider>
        <TestComponent />
      </AuthProvider>
    );

    // After resolution, it should show the tenant ID
    await waitFor(() => {
      expect(screen.queryByTestId('loading')).not.toBeInTheDocument();
      expect(screen.getByTestId('tenant-id')).toHaveTextContent('tenant-123');
    });
  });

  it('sets tenant to null if session check fails', async () => {
    // Mock failure for the initial session check
    axios.get.mockRejectedValueOnce(new Error('Unauthorized'));

    render(
      <AuthProvider>
        <TestComponent />
      </AuthProvider>
    );

    await waitFor(() => {
      expect(screen.getByTestId('tenant-id')).toHaveTextContent('No Tenant');
    });
  });

  it('login updates the tenant state on success', async () => {
    // Initial load fails (not logged in)
    axios.get.mockRejectedValueOnce(new Error('Unauthorized'));
    
    // Login post succeeds
    axios.post.mockResolvedValueOnce({
      data: { tenantId: 'brand-new-tenant', businessName: 'New Business' }
    });

    render(
      <AuthProvider>
        <TestComponent />
      </AuthProvider>
    );

    // Wait for initial load to finish
    await waitFor(() => expect(screen.getByTestId('tenant-id')).toHaveTextContent('No Tenant'));

    // Trigger login
    screen.getByTestId('login-btn').click();

    await waitFor(() => {
      expect(screen.getByTestId('tenant-id')).toHaveTextContent('brand-new-tenant');
    });
  });

  it('logout clears the tenant state', async () => {
    // Initial load succeeds
    axios.get.mockResolvedValueOnce({
      data: { tenantId: 'tenant-123', businessName: 'Test Business' }
    });
    axios.post.mockResolvedValueOnce({}); // Logout post succeeds

    render(
      <AuthProvider>
        <TestComponent />
      </AuthProvider>
    );

    await waitFor(() => expect(screen.getByTestId('tenant-id')).toHaveTextContent('tenant-123'));

    // Trigger logout
    screen.getByTestId('logout-btn').click();

    await waitFor(() => {
      expect(screen.getByTestId('tenant-id')).toHaveTextContent('No Tenant');
    });
  });
});
