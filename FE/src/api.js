export const API_BASE = (import.meta.env?.VITE_API_BASE || 'http://localhost:5088').replace(/\/$/, '');

export class ApiError extends Error {
  constructor(message, status = 0, retryAfterSeconds = null) { super(message); this.name = 'ApiError'; this.status = status; this.retryAfterSeconds = retryAfterSeconds; }
}

export function retryAfterSeconds(value, now = Date.now()) {
  if (!value?.trim()) return null;
  if (/^\d+$/.test(value.trim())) { const seconds = Number(value); return Number.isSafeInteger(seconds) ? seconds : null; }
  const date = Date.parse(value);
  return Number.isFinite(date) ? Math.max(0, Math.ceil((date - now) / 1000)) : null;
}

function errorMessage(data, response) {
  const validation = data?.errors && Object.values(data.errors).flat().filter(Boolean).join(' ');
  return data?.error || validation || data?.detail || data?.title || `Yêu cầu thất bại (HTTP ${response.status})`;
}

async function request(path, { token, method = 'GET', body, formData, signal } = {}) {
  const headers = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  let response;
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method, headers, signal, body: formData || (body !== undefined ? JSON.stringify(body) : undefined),
    });
  } catch (error) {
    if (error.name === 'AbortError') throw error;
    throw new ApiError('Không kết nối được máy chủ. Kiểm tra kết nối mạng và thử lại.');
  }
  if (!response.ok) {
    let data;
    try { data = await response.json(); } catch { /* A proxy may return a non-JSON error. */ }
    if (response.status === 401 && token && typeof window !== 'undefined') {
      window.dispatchEvent(new CustomEvent('lms-unauthorized', { detail: { token } }));
    }
    const wait = retryAfterSeconds(response.headers.get('Retry-After'));
    const message = errorMessage(data, response);
    throw new ApiError(wait > 0 && [429, 503].includes(response.status) ? `${message} Vui lòng chờ ít nhất ${wait} giây trước khi gửi lại.` : message, response.status, wait);
  }
  return response;
}

export async function api(path, options) {
  const response = await request(path, options);
  if (response.status === 204) return null;
  const content = await response.text();
  if (!content) return null;
  try { return JSON.parse(content); }
  catch { throw new ApiError('Máy chủ trả dữ liệu không hợp lệ.', response.status); }
}

export function contentDispositionFilename(value) {
  if (!value) return null;
  const names = [];
  const extended = /(?:^|;)\s*filename\*\s*=\s*(?:"([^"]*)"|([^;]*))/i.exec(value);
  const encoded = extended && /^UTF-8'[^']*'(.*)$/i.exec((extended[1] ?? extended[2]).trim());
  if (encoded) {
    try { names.push(decodeURIComponent(encoded[1])); } catch { /* Use the plain filename if encoding is malformed. */ }
  }
  const plain = /(?:^|;)\s*filename\s*=\s*(?:"((?:\\.|[^"\\])*)"|([^;]*))/i.exec(value);
  if (plain) names.push((plain[1] ?? plain[2]).trim().replace(/\\(["\\])/g, '$1'));
  for (const name of names) {
    const basename = name.split(/[\\/]/).pop().replace(/[\u0000-\u001f\u007f<>:"|?*]/g, '_').trim().replace(/[. ]+$/g, '');
    if (basename && basename !== '.') return basename;
  }
  return null;
}

export async function downloadMaterial(id, token, filename) {
  const response = await request(`/api/materials/${id}/download`, { token });
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url; link.download = contentDispositionFilename(response.headers.get('Content-Disposition')) || filename || `material-${id}`;
  document.body.appendChild(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export async function readMaterial(id, token, signal) {
  const response = await request(`/api/materials/${id}/download`, { token, signal });
  const blob = await response.blob();
  if (signal?.aborted) throw new DOMException('Canceled', 'AbortError');
  if (blob.type.includes('pdf')) return { kind: 'pdf', url: URL.createObjectURL(blob) };
  if (blob.type.startsWith('text/')) return { kind: 'text', text: await blob.text() };
  throw new ApiError('Định dạng này hỗ trợ xem nội dung trích xuất hoặc tải tệp gốc.');
}
