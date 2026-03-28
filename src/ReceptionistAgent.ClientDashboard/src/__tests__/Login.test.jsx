import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import Login from '../pages/Login';
import { useAuth } from '../context/AuthContext';
import { useNavigate } from 'react-router-dom';
import React from 'react';

// Mocks
vi.mock('../context/AuthContext', () => ({
  useAuth: vi.fn(),
}));

vi.mock('react-router-dom', () => ({
  useNavigate: vi.fn(),
}));

// Mock Lucide components
vi.mock('lucide-react', () => ({
  AlertCircle: () => <div data-testid="alert-icon" />,
}));

describe('Login Component', () => {
  const mockLogin = vi.fn();
  const mockNavigate = vi.fn();

  beforeEach(() => {
    vi.resetAllMocks();
    useAuth.mockReturnValue({ login: mockLogin });
    useNavigate.mockReturnValue(mockNavigate);
  });

  it('renders login form correctly', () => {
    render(<Login />);
    expect(screen.getByText(/Bienvenido de nuevo/i)).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/nombre@empresa.com/i)).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/••••••••/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Iniciar sesión/i })).toBeInTheDocument();
  });

  it('updates state on input change', () => {
    render(<Login />);
    const usernameInput = screen.getByPlaceholderText(/nombre@empresa.com/i);
    const passwordInput = screen.getByPlaceholderText(/••••••••/i);

    fireEvent.change(usernameInput, { target: { value: 'testuser' } });
    fireEvent.change(passwordInput, { target: { value: 'password123' } });

    expect(usernameInput.value).toBe('testuser');
    expect(passwordInput.value).toBe('password123');
  });

  it('submits correctly with username and password and navigates on success', async () => {
    mockLogin.mockResolvedValue({ success: true });
    render(<Login />);

    fireEvent.change(screen.getByPlaceholderText(/nombre@empresa.com/i), { target: { value: 'test@user.com' } });
    fireEvent.change(screen.getByPlaceholderText(/••••••••/i), { target: { value: 'password123' } });
    fireEvent.click(screen.getByRole('button', { name: /Iniciar sesión/i }));

    await waitFor(() => {
      expect(mockLogin).toHaveBeenCalledWith('test@user.com', 'password123');
      expect(mockNavigate).toHaveBeenCalledWith('/inbox');
    });
  });

  it('shows error message on login failure', async () => {
    mockLogin.mockResolvedValue({ success: false, error: 'Credenciales inválidas' });
    render(<Login />);

    fireEvent.change(screen.getByPlaceholderText(/nombre@empresa.com/i), { target: { value: 'test@user.com' } });
    fireEvent.change(screen.getByPlaceholderText(/••••••••/i), { target: { value: 'wrong' } });
    fireEvent.click(screen.getByRole('button', { name: /Iniciar sesión/i }));

    await waitFor(() => {
      expect(screen.getByText(/Credenciales inválidas/i)).toBeInTheDocument();
    });
  });

  it('shows generic error on exception', async () => {
    mockLogin.mockRejectedValue(new Error('Network error'));
    render(<Login />);

    // Llenar campos requeridos para evitar validación de navegador si existiera
    fireEvent.change(screen.getByPlaceholderText(/nombre@empresa.com/i), { target: { value: 'test@user.com' } });
    fireEvent.change(screen.getByPlaceholderText(/••••••••/i), { target: { value: 'password123' } });

    const form = screen.getByRole('button', { name: /Iniciar sesión/i }).closest('form');
    fireEvent.submit(form);

    await waitFor(() => {
      expect(screen.getByText(/Ocurrió un error inesperado/i)).toBeInTheDocument();
    }, { timeout: 2000 });
  });
});
