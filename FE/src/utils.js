export const ROLES = { Lecturer: 'Giảng viên', Student: 'Sinh viên', Admin: 'Quản trị viên' };
// EF dates are stored in UTC; SQLite may return an ISO value without a zone.
export function apiTimestamp(value) {
  if (value == null || value === '') return NaN;
  const normalized = typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?$/.test(value) ? `${value}Z` : value;
  return new Date(normalized).getTime();
}
export function formatTime(value) {
  const timestamp = apiTimestamp(value);
  return Number.isFinite(timestamp) ? new Date(timestamp).toLocaleString('vi-VN') : '—';
}
export const emptyQuestion = () => ({ content: '', explanation: '', difficulty: '', options: ['', '', '', ''], correct: 0 });
export const DIFFICULTIES = { Easy: 'Dễ', Medium: 'Trung bình', Hard: 'Khó' };
export const formatScore = value => value == null ? 'Chưa có bài nộp' : `${Number(value).toLocaleString('vi-VN', { maximumFractionDigits: 2 })}/10`;

export function filterQuestions(questions, { search = '', status = '', topicId = '', source = '', difficulty = '' } = {}) {
  const term = search.trim().toLocaleLowerCase('vi-VN');
  return questions.filter(question => (!status || question.status === status)
    && (!topicId || String(question.topicId) === String(topicId))
    && (!source || question.source === source)
    && (!difficulty || (difficulty === 'Unspecified' ? !question.difficulty : question.difficulty === difficulty))
    && question.content.toLocaleLowerCase('vi-VN').includes(term));
}

export function validateAccount({ username, fullName, password }) {
  if (!/^[a-z0-9_.-]{3,40}$/.test(username.trim().toLowerCase())) return 'Tên đăng nhập cần 3–40 ký tự: a–z, 0–9, dấu chấm, gạch dưới hoặc gạch ngang.';
  if (fullName.trim().length < 2 || fullName.trim().length > 100) return 'Họ tên cần 2–100 ký tự.';
  if (password.length < 8 || password.length > 128) return 'Mật khẩu cần 8–128 ký tự.';
  return '';
}

export function validateProfile({ fullName, email = '' }) {
  if (fullName.trim().length < 2 || fullName.trim().length > 100) return 'Họ tên cần 2–100 ký tự.';
  if (email.trim() && (email.trim().length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()))) return 'Nhập địa chỉ email hợp lệ (tối đa 254 ký tự).';
  return '';
}

export function validateQuestion(question) {
  if (!question.content.trim()) return 'Nhập nội dung câu hỏi.';
  if (question.options.length !== 4 || question.options.some(option => !option.trim())) return 'Nhập đủ 4 phương án không rỗng.';
  if (!Number.isInteger(question.correct) || question.correct < 0 || question.correct > 3) return 'Chọn đúng một đáp án.';
  if (new Set(question.options.map(option => option.trim().toLowerCase())).size !== 4) return 'Các phương án cần khác nhau.';
  if (question.difficulty && !DIFFICULTIES[question.difficulty]) return 'Chọn độ khó hợp lệ.';
  return '';
}

export const MATERIAL_EXTENSIONS = ['.txt', '.pdf', '.docx', '.pptx', '.md'];
export function formatFileSize(value) {
  if (value == null || !Number.isFinite(Number(value))) return 'Chưa có dữ liệu dung lượng';
  const size = Number(value);
  if (size < 1000) return `${size.toLocaleString('vi-VN')} B`;
  return `${(size / (size < 1000000 ? 1000 : 1000000)).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} ${size < 1000000 ? 'KB' : 'MB'}`;
}
export function validateUpload(file, maxBytes = 5000000, extensions = MATERIAL_EXTENSIONS) {
  if (!file) return 'Chọn tệp học liệu.';
  const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
  if (!extensions.includes(extension)) return `Chỉ hỗ trợ ${extensions.join(', ')}.`;
  if (file.size === 0 || file.size > maxBytes) return `Tệp cần có nội dung và không vượt quá ${formatFileSize(maxBytes)}.`;
  return '';
}

export function sessionExpiry(session) {
  if (session?.expiresAtUtc) return apiTimestamp(session.expiresAtUtc) || 0;
  try {
    const tokenPart = session.accessToken.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const expires = JSON.parse(atob(tokenPart)).exp * 1000;
    return Number.isFinite(expires) ? expires : 0;
  } catch { return 0; }
}

export function submissionAnswers(quiz, selected) {
  if (!quiz.questions.length || quiz.questions.some(question => !question.options.some(option => option.id === Number(selected[question.id])))) {
    throw new Error('Bạn cần chọn một phương án cho tất cả câu hỏi trước khi nộp.');
  }
  return quiz.questions.map(question => ({ questionId: question.id, selectedOptionId: Number(selected[question.id]) }));
}
