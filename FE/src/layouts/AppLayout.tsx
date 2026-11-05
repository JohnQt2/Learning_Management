import { useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import type { Role } from '../types';
import AiAssistant from '../components/AiAssistant';

const menus: Record<Role, { to: string; label: string }[]> = {
  student: [{ to: '/student/courses', label: 'Môn học của tôi' }],
  lecturer: [{ to: '/lecturer/grading', label: 'Chấm bài' }],
  admin: [{ to: '/admin/users', label: 'Người dùng' }],
};

const roleLabel: Record<Role, string> = {
  student: 'Sinh viên',
  lecturer: 'Giảng viên',
  admin: 'Quản trị viên',
};

export default function AppLayout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [aiOpen, setAiOpen] = useState(false);
  if (!user) return null;

  const canUseAi = user.role !== 'admin';

  return (
    <div className="shell">
      <nav className="sidebar" aria-label="Điều hướng chính">
        <div className="brand">Lớp học số</div>
        <ul>
          {menus[user.role].map((item) => (
            <li key={item.to}>
              <NavLink to={item.to} className={({ isActive }) => (isActive ? 'nav active' : 'nav')}>
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>

      <div className="main">
        <header className="topbar">
          <div>
            <strong>{user.name}</strong>
            <span className="role-tag">{roleLabel[user.role]}</span>
          </div>
          <div className="topbar-actions">
            {canUseAi && (
              <button className="btn-ai" onClick={() => setAiOpen((v) => !v)}>
                Hỏi trợ lý AI
              </button>
            )}
            <button
              className="btn-ghost"
              onClick={() => {
                logout();
                navigate('/login');
              }}
            >
              Đăng xuất
            </button>
          </div>
        </header>

        <main className="content">
          <Outlet />
        </main>
      </div>

      {aiOpen && canUseAi && <AiAssistant onClose={() => setAiOpen(false)} />}
    </div>
  );
}
