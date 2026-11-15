import { Navigate, Outlet } from 'react-router-dom';
import type { Role } from '../types';
import { roleHome, useAuth } from './AuthContext';

// Không truyền roles: chỉ cần đã đăng nhập. Có roles: phải đúng vai trò.
export default function ProtectedRoute({ roles }: { roles?: Role[] }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  if (roles && !roles.includes(user.role)) return <Navigate to={roleHome[user.role]} replace />;
  return <Outlet />;
}
