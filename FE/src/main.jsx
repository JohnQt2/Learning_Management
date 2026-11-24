import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api } from './api.js';
import { AuthPage, Profile } from './pages/Account.jsx';
import { AdminPage } from './pages/Admin.jsx';
import { CourseWorkspace } from './pages/Courses.jsx';
import { LecturerDashboard, StudentDashboard } from './pages/Dashboard.jsx';
import { StudentHistory, Recommendations } from './pages/Results.jsx';
import { LoadState, Notice, Tag, useAction, useLoad } from './shared.jsx';
import { ROLES, sessionExpiry } from './utils.js';
import './styles.css';

function readSession() {
  try {
    const saved = JSON.parse(sessionStorage.getItem('lms-session') || 'null');
    if (!saved?.accessToken || !ROLES[saved.role] || sessionExpiry(saved) <= Date.now()) {
      sessionStorage.removeItem('lms-session'); return null;
    }
    return saved;
  } catch { return null; }
}

function App() {
  const [session, setSession] = useState(readSession);
  const [notice, setNotice] = useState('');
  function signIn(data) {
    sessionStorage.setItem('lms-session', JSON.stringify(data));
    setNotice(''); setSession(data);
  }
  function signOut(message = 'Bạn đã đăng xuất.') {
    sessionStorage.removeItem('lms-session'); setSession(null); setNotice(message);
  }
  useEffect(() => {
    if (!session) return;
    const unauthorized = event => { if (event.detail.token === session.accessToken) signOut('Phiên đăng nhập đã hết hạn hoặc bị thu hồi. Vui lòng đăng nhập lại.'); };
    window.addEventListener('lms-unauthorized', unauthorized);
    const timeout = setTimeout(() => signOut('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.'), Math.max(0, sessionExpiry(session) - Date.now()));
    return () => { clearTimeout(timeout); window.removeEventListener('lms-unauthorized', unauthorized); };
  }, [session]);

  return <div className="shell"><header className="topbar"><div className="brand"><div className="logo">L</div><div><strong>LMS<span>AI</span></strong><small>HỌC TẬP VÀ KẾT NỐI</small></div></div><div className="topright">Nền tảng quản lý học tập</div></header>
    <main className="container">{!session ? <><div className="intro"><div><div className="eyebrow">LEARNING MANAGEMENT SYSTEM</div><h1>Chào mừng đến với LMS-AI</h1><p>Khóa học, học liệu và bài kiểm tra trong cùng một không gian.</p></div></div><Notice message={notice}/><AuthPage onSignIn={signIn}/></> : <Authenticated key={session.accessToken} session={session} signOut={signOut}/>}
    <footer><span>LMS-AI · Hệ thống quản lý học tập</span><span>Gợi ý ôn tập theo kết quả thực tế</span></footer></main></div>;
}

function Authenticated({ session, signOut }) {
  const { accessToken: token, role } = session;
  const profile = useLoad(signal => api('/api/auth/me', { token, signal }), [token]);
  const [view, setView] = useState(role === 'Admin' ? 'admin' : 'dashboard');
  const [courseTarget, setCourseTarget] = useState({ id: '', tab: 'content' });
  function openCourse(id, tab = 'content') { setCourseTarget({ id: String(id), tab }); setView('courses'); }
  const action = useAction();
  async function logout() {
    await action.run(async () => {
      try { await api('/api/auth/logout', { token, method: 'POST' }); signOut(); }
      catch { signOut('Đã đăng xuất trên thiết bị này. Không xác nhận được việc kết thúc phiên trên máy chủ do lỗi kết nối hoặc phiên đã hết hạn.'); }
    });
  }
  return <LoadState resource={profile}>{profile.data && <><div className="intro"><div><div className="eyebrow">KHÔNG GIAN {ROLES[role].toUpperCase()}</div><h1>Xin chào, {profile.data.fullName || session.displayName}</h1><p>{profile.data.username} <Tag>{ROLES[role]}</Tag></p></div><button className="btn outline" onClick={logout} disabled={action.busy}>Đăng xuất</button></div><Notice {...action}/>
    <nav className="main-nav" aria-label="Điều hướng chính">{(role === 'Admin' ? [['admin', 'Quản lý tài khoản']] : [['dashboard', 'Tổng quan'], ['courses', 'Khóa học'], ...(role === 'Student' ? [['history', 'Lịch sử bài làm'], ['recommendations', 'Gợi ý ôn tập']] : [])]).concat([['profile', 'Tài khoản']]).map(([key, label]) => <button key={key} className={`nav-button ${view === key ? 'active' : ''}`} onClick={() => { setView(key); if (key === 'profile') profile.reload(); }} aria-current={view === key ? 'page' : undefined}>{label}</button>)}</nav>
    {view === 'profile' && <Profile profile={profile.data} token={token} signOut={signOut} onSaved={profile.reload}/>}
    {view === 'admin' && role === 'Admin' && <AdminPage token={token} actorId={session.userId}/>}
    {view === 'dashboard' && role === 'Student' && <StudentDashboard token={token} onOpenCourse={openCourse}/>}
    {view === 'dashboard' && role === 'Lecturer' && <LecturerDashboard token={token} onOpenCourse={openCourse}/>}
    {view === 'courses' && role !== 'Admin' && <CourseWorkspace token={token} role={role} initialCourseId={courseTarget.id} initialTab={courseTarget.tab}/>}
    {view === 'history' && role === 'Student' && <StudentHistory token={token}/>}
    {view === 'recommendations' && role === 'Student' && <Recommendations token={token}/>}
  </>}</LoadState>;
}

createRoot(document.getElementById('root')).render(<App/>);
