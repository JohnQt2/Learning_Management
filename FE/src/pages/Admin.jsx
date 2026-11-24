import React, { useState } from 'react';
import { api } from '../api.js';
import { Empty, Field, LoadState, Notice, Panel, Tag, useAction, useLoad } from '../shared.jsx';
import { formatTime, ROLES, validateAccount, validateProfile } from '../utils.js';
import { AccountFields } from './Account.jsx';

export function AdminPage({ token, actorId }) {
  const [page, setPage] = useState(1), [selectedId, setSelectedId] = useState(null);
  const [account, setAccount] = useState({ username: '', fullName: '', email: '', password: '' });
  const [search, setSearch] = useState({ search: '', role: '', isLocked: '' });
  const [filters, setFilters] = useState(search);
  const query = new URLSearchParams({ page: String(page), pageSize: '20' });
  Object.entries(filters).forEach(([key, value]) => { if (value !== '') query.set(key, value); });
  const users = useLoad(signal => api(`/api/admin/users?${query}`, { token, signal }), [token, page, filters.search, filters.role, filters.isLocked]);
  const action = useAction();
  async function create(event) {
    event.preventDefault();
    await action.run(async () => {
      const error = validateAccount(account); if (error) throw new Error(error);
      const profileError = validateProfile(account); if (profileError) throw new Error(profileError);
      await api('/api/admin/users/lecturers', { token, method: 'POST', body: { ...account, username: account.username.trim(), fullName: account.fullName.trim(), email: account.email.trim() || null } });
      setAccount({ username: '', fullName: '', email: '', password: '' }); users.reload();
    }, 'Đã tạo tài khoản giảng viên.');
  }
  async function lock(user) {
    await action.run(async () => {
      await api(`/api/admin/users/${user.id}/lock`, { token, method: 'PATCH', body: { isLocked: !user.isLocked } });
      users.reload();
    }, user.isLocked ? 'Đã mở khóa tài khoản.' : 'Đã khóa tài khoản. Các phiên đăng nhập cũ bị thu hồi.');
  }
  return <><Panel title="Tạo tài khoản giảng viên" subtitle="Thông tin đăng nhập cần được chuyển riêng cho giảng viên."><Notice {...action}/><form onSubmit={create}><fieldset className="formgrid" disabled={action.busy}><AccountFields value={account} onChange={setAccount}/><button className="btn primary">Tạo giảng viên</button></fieldset></form></Panel>
    <Panel title="Danh sách tài khoản" subtitle={users.data ? `${users.data.total} tài khoản khớp bộ lọc · Trang ${page}` : 'Quản lý trạng thái tài khoản trong hệ thống.'}>
      <form onSubmit={event => { event.preventDefault(); setFilters({ ...search, search: search.search.trim() }); setPage(1); setSelectedId(null); }}><fieldset className="formgrid filters" disabled={action.busy}><Field label="Tìm tên, username hoặc email"><input type="search" maxLength={150} value={search.search} onChange={event => setSearch({ ...search, search: event.target.value })}/></Field><Field label="Vai trò"><select value={search.role} onChange={event => setSearch({ ...search, role: event.target.value })}><option value="">Tất cả vai trò</option>{Object.entries(ROLES).map(([role, label]) => <option key={role} value={role}>{label}</option>)}</select></Field><Field label="Trạng thái tài khoản"><select value={search.isLocked} onChange={event => setSearch({ ...search, isLocked: event.target.value })}><option value="">Tất cả trạng thái</option><option value="false">Hoạt động</option><option value="true">Đã khóa</option></select></Field><div className="actions"><button className="btn primary">Áp dụng bộ lọc</button><button className="btn outline" type="button" onClick={() => { const empty = { search: '', role: '', isLocked: '' }; setSearch(empty); setFilters(empty); setPage(1); setSelectedId(null); }}>Xóa bộ lọc</button></div></fieldset></form>
      <LoadState resource={users}>{users.data && <><div className="itemlist">{users.data.items.length ? users.data.items.map(user => <div className="item" key={user.id}><div><b>{user.fullName}</b><small>{user.username} · {ROLES[user.role]} · #{user.id}</small><small>{user.email || 'Chưa có email'} · Tạo: {user.createdAt ? formatTime(user.createdAt) : 'Chưa có dữ liệu'}</small></div><Tag ok={!user.isLocked}>{user.isLocked ? 'Đã khóa' : 'Hoạt động'}</Tag><div className="actions"><button className="btn outline" disabled={action.busy || users.loading} onClick={() => setSelectedId(user.id)}>Thông tin</button><button className="btn outline" disabled={action.busy || users.loading || user.id === actorId} onClick={() => lock(user)}>{user.isLocked ? 'Mở khóa' : 'Khóa'}</button></div></div>) : <Empty>Không có tài khoản khớp bộ lọc.</Empty>}</div><div className="pagination"><button className="btn outline" disabled={page === 1 || action.busy || users.loading} onClick={() => setPage(page - 1)}>Trang trước</button><span>Trang {page} / {Math.max(1, Math.ceil(users.data.total / 20))}</span><button className="btn outline" disabled={page * 20 >= users.data.total || action.busy || users.loading} onClick={() => setPage(page + 1)}>Trang sau</button></div></>}</LoadState></Panel>
    {selectedId && <UserDetail key={selectedId} token={token} id={selectedId} locked={users.data?.items.find(user => user.id === selectedId)?.isLocked} onClose={() => setSelectedId(null)}/>}
  </>;
}

function UserDetail({ token, id, locked, onClose }) {
  const user = useLoad(signal => api(`/api/admin/users/${id}`, { token, signal }), [token, id, locked]);
  return <Panel title={`Thông tin tài khoản #${id}`}><button className="btn outline" onClick={onClose}>Đóng thông tin</button><LoadState resource={user}>{user.data && <dl className="details"><dt>Họ tên</dt><dd>{user.data.fullName}</dd><dt>Tên đăng nhập</dt><dd>{user.data.username}</dd><dt>Email</dt><dd>{user.data.email || 'Chưa có email'}</dd><dt>Vai trò</dt><dd>{ROLES[user.data.role]}</dd><dt>Ngày tạo</dt><dd>{user.data.createdAt ? formatTime(user.data.createdAt) : 'Chưa có dữ liệu'}</dd><dt>Trạng thái</dt><dd>{user.data.isLocked ? 'Đã khóa' : 'Hoạt động'}</dd></dl>}</LoadState></Panel>;
}
