import React, { useState } from 'react';
import { api } from '../api.js';
import { Empty, Field, Notice, Panel, Tag, useAction } from '../shared.jsx';
import { formatTime, ROLES, validateAccount, validateProfile } from '../utils.js';

export function AccountFields({ value, onChange, registration = true }) {
  const change = key => event => onChange({ ...value, [key]: event.target.value });
  return <><Field label="Tên đăng nhập"><input required autoComplete="username" minLength={3} maxLength={40} value={value.username} onChange={change('username')} spellCheck="false"/></Field>
    {registration && <><Field label="Họ và tên"><input required autoComplete="name" minLength={2} maxLength={100} value={value.fullName} onChange={change('fullName')}/></Field><Field label="Email (không bắt buộc)"><input type="email" autoComplete="email" maxLength={254} value={value.email || ''} onChange={change('email')}/></Field></>}
    <Field label={registration ? 'Mật khẩu (8–128 ký tự)' : 'Mật khẩu'}><input required type="password" autoComplete={registration ? 'new-password' : 'current-password'} minLength={registration ? 8 : 1} maxLength={128} value={value.password} onChange={change('password')}/></Field></>;
}

export function AuthPage({ onSignIn }) {
  const [mode, setMode] = useState('login');
  const [credentials, setCredentials] = useState({ username: '', fullName: '', email: '', password: '' });
  const [confirmation, setConfirmation] = useState('');
  const action = useAction();
  async function submit(event) {
    event.preventDefault();
    await action.run(async () => {
      if (mode === 'register') {
        const error = validateAccount(credentials);
        if (error) throw new Error(error);
        const profileError = validateProfile(credentials); if (profileError) throw new Error(profileError);
        if (credentials.password !== confirmation) throw new Error('Mật khẩu xác nhận không khớp.');
      }
      const body = { username: credentials.username.trim(), password: credentials.password, ...(mode === 'register' ? { fullName: credentials.fullName.trim(), email: credentials.email.trim() || null } : {}) };
      const data = await api(`/api/auth/${mode}`, { method: 'POST', body });
      setCredentials({ username: '', fullName: '', email: '', password: '' }); setConfirmation('');
      onSignIn(data);
    });
  }
  return <div className="auth-layout"><Panel title={mode === 'register' ? 'Đăng ký sinh viên' : 'Đăng nhập'} subtitle="Sử dụng tài khoản của bạn để tiếp tục học tập.">
    <Notice {...action}/><form className="stack" onSubmit={submit}><fieldset disabled={action.busy} className="stack"><AccountFields value={credentials} onChange={setCredentials} registration={mode === 'register'}/>
      {mode === 'register' && <Field label="Nhập lại mật khẩu"><input required type="password" autoComplete="new-password" minLength={8} maxLength={128} value={confirmation} onChange={event => setConfirmation(event.target.value)}/></Field>}
      <button className="btn primary">{mode === 'register' ? 'Tạo tài khoản sinh viên' : 'Đăng nhập'}</button></fieldset></form>
    <button className="btn outline text-link" disabled={action.busy} onClick={() => { setMode(mode === 'login' ? 'register' : 'login'); setCredentials({ username: '', fullName: '', email: '', password: '' }); setConfirmation(''); }}>{mode === 'login' ? 'Chưa có tài khoản? Đăng ký sinh viên' : 'Đã có tài khoản? Đăng nhập'}</button>
  </Panel><Panel title="Một không gian học tập chung" subtitle="Dành cho sinh viên, giảng viên và quản trị viên."><ul className="feature-list"><li>Theo dõi khóa học và tài liệu của bạn.</li><li>Làm bài kiểm tra và xem kết quả chi tiết.</li><li>Ôn tập những chủ đề cần cải thiện.</li><li>Giảng viên soạn câu hỏi hoặc sinh bản nháp từ nội dung học liệu bằng Gemini khi được cấu hình.</li></ul><p className="muted">Tài khoản giảng viên được quản trị viên cấp.</p></Panel></div>;
}

export function Profile({ profile, token, signOut, onSaved }) {
  const [info, setInfo] = useState({ fullName: profile.fullName, email: profile.email || '' });
  const profileAction = useAction();
  const [passwords, setPasswords] = useState({ currentPassword: '', newPassword: '', confirmation: '' });
  const action = useAction();
  async function saveProfile(event) {
    event.preventDefault();
    await profileAction.run(async () => {
      const error = validateProfile(info); if (error) throw new Error(error);
      const updated = await api('/api/auth/me', { token, method: 'PUT', body: { fullName: info.fullName.trim(), email: info.email.trim() || null } });
      setInfo({ fullName: updated.fullName, email: updated.email || '' }); onSaved();
    }, 'Đã cập nhật hồ sơ.');
  }
  async function changePassword(event) {
    event.preventDefault();
    await action.run(async () => {
      if (passwords.newPassword !== passwords.confirmation) throw new Error('Mật khẩu xác nhận không khớp.');
      if (passwords.newPassword === passwords.currentPassword) throw new Error('Mật khẩu mới cần khác mật khẩu hiện tại.');
      await api('/api/auth/change-password', { token, method: 'POST', body: { currentPassword: passwords.currentPassword, newPassword: passwords.newPassword } });
      signOut('Đổi mật khẩu thành công. Vui lòng đăng nhập lại bằng mật khẩu mới.');
    });
  }
  return <><div className="two-columns"><Panel title="Thông tin tài khoản"><dl className="details"><dt>Tên đăng nhập</dt><dd>{profile.username}</dd><dt>Vai trò</dt><dd>{ROLES[profile.role]}</dd><dt>Mã tài khoản</dt><dd>#{profile.userId}</dd><dt>Ngày tạo</dt><dd>{profile.createdAt ? formatTime(profile.createdAt) : 'Chưa có dữ liệu'}</dd><dt>Trạng thái</dt><dd><Tag ok={!profile.isLocked}>{profile.isLocked ? 'Đã khóa' : 'Hoạt động'}</Tag></dd></dl><Notice {...profileAction}/><form onSubmit={saveProfile}><fieldset className="stack" disabled={profileAction.busy}><Field label="Họ và tên"><input required autoComplete="name" minLength={2} maxLength={100} value={info.fullName} onChange={event => setInfo({ ...info, fullName: event.target.value })}/></Field><Field label="Email (không bắt buộc)"><input type="email" autoComplete="email" maxLength={254} value={info.email} onChange={event => setInfo({ ...info, email: event.target.value })}/></Field><button className="btn primary">Lưu hồ sơ</button></fieldset></form></Panel>
    <Panel title="Đổi mật khẩu" subtitle="Sau khi đổi mật khẩu, các phiên đăng nhập sẽ kết thúc."><Notice {...action}/><form onSubmit={changePassword}><fieldset disabled={action.busy} className="stack">{[['currentPassword', 'Mật khẩu hiện tại'], ['newPassword', 'Mật khẩu mới (8–128 ký tự)'], ['confirmation', 'Xác nhận mật khẩu mới']].map(([key, label]) => <Field key={key} label={label}><input required type="password" autoComplete={key === 'currentPassword' ? 'current-password' : 'new-password'} minLength={key === 'currentPassword' ? 1 : 8} maxLength={128} value={passwords[key]} onChange={event => setPasswords({ ...passwords, [key]: event.target.value })}/></Field>)}<button className="btn primary">Đổi mật khẩu</button></fieldset></form></Panel></div>
    {profile.role !== 'Admin' && <Panel title={profile.role === 'Lecturer' ? 'Khóa học đang giảng dạy' : 'Khóa học đã tham gia'}><div className="itemlist">{profile.courses?.length ? profile.courses.map(course => <div className="item" key={course.courseId}><div><b>{course.code} · {course.title}</b><small>{course.relationship === 'Teaching' ? 'Giảng dạy' : `Tham gia ${formatTime(course.enrolledAt)}`}</small></div><Tag>#{course.courseId}</Tag></div>) : <Empty>Chưa có khóa học.</Empty>}</div></Panel>}</>;
}
