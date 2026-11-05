import test, { afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { api, ApiError, API_BASE, contentDispositionFilename, downloadMaterial, readMaterial, retryAfterSeconds } from '../src/api.js';
import { apiTimestamp, emptyQuestion, filterQuestions, formatScore, formatTime, sessionExpiry, submissionAnswers, validateAccount, validateProfile, validateQuestion, validateUpload } from '../src/utils.js';

const originalFetch = globalThis.fetch;
afterEach(() => { globalThis.fetch = originalFetch; delete globalThis.window; });

test('authenticated JSON mutation sends bearer token and only caller request fields', async () => {
  let request;
  globalThis.fetch = async (url, options) => { request = { url, ...options }; return new Response(JSON.stringify({ attemptId: 9, score: 5 }), { status: 201 }); };
  assert.deepEqual(await api('/api/quizzes/2/submit', { token: 'token-fixture', method: 'POST', body: { answers: [{ questionId: 3, selectedOptionId: 4 }] } }), { attemptId: 9, score: 5 });
  assert.equal(request.url, `${API_BASE}/api/quizzes/2/submit`);
  assert.equal(request.headers.Authorization, 'Bearer token-fixture');
  assert.equal(request.headers['Content-Type'], 'application/json');
  assert.deepEqual(JSON.parse(request.body), { answers: [{ questionId: 3, selectedOptionId: 4 }] });
});

test('204 password and logout responses are valid successes without JSON', async () => {
  globalThis.fetch = async () => new Response(null, { status: 204 });
  assert.equal(await api('/api/auth/logout', { token: 'token', method: 'POST' }), null);
});

test('ASP.NET validation errors are shown with HTTP status retained', async () => {
  globalThis.fetch = async () => new Response(JSON.stringify({ title: 'Validation failed', errors: { Title: ['Tên khóa học không hợp lệ.'], Name: ['Tên quá dài.'] } }), { status: 400 });
  await assert.rejects(api('/api/courses'), error => error instanceof ApiError && error.status === 400 && error.message === 'Tên khóa học không hợp lệ. Tên quá dài.');
});

test('backend business/quota error message survives non-success response', async () => {
  globalThis.fetch = async () => new Response(JSON.stringify({ error: 'Gemini đã hết quota.' }), { status: 429 });
  await assert.rejects(api('/api/ai/questions/generate'), error => error.status === 429 && error.message === 'Gemini đã hết quota.');
});

test('an authenticated 401 revokes only the token associated with that request', async () => {
  let event;
  globalThis.window = { dispatchEvent: value => { event = value; } };
  globalThis.fetch = async () => new Response(JSON.stringify({ error: 'Phiên hết hạn.' }), { status: 401 });
  await assert.rejects(api('/api/auth/me', { token: 'old-session' }), error => error.status === 401);
  assert.equal(event.type, 'lms-unauthorized'); assert.equal(event.detail.token, 'old-session');
});

test('public bad password never emits session invalidation', async () => {
  let count = 0;
  globalThis.window = { dispatchEvent: () => { count++; } };
  globalThis.fetch = async () => new Response('{}', { status: 401 });
  await assert.rejects(api('/api/auth/login', { method: 'POST', body: { username: 'student', password: 'bad' } }));
  assert.equal(count, 0);
});

test('multipart upload leaves content boundary generation to fetch', async () => {
  const body = new FormData(); body.append('file', new Blob(['Học liệu'], { type: 'text/plain' }), 'material.txt'); body.append('topicId', '2');
  globalThis.fetch = async (_url, request) => {
    assert.equal(request.body, body); assert.equal(request.headers['Content-Type'], undefined);
    return new Response('{"id":1}', { status: 201 });
  };
  assert.deepEqual(await api('/api/courses/1/materials', { token: 'token', method: 'POST', formData: body }), { id: 1 });
});

test('canceled old page request preserves AbortError rather than reporting a network failure', async () => {
  const signal = new AbortController().signal;
  globalThis.fetch = async (_url, request) => { assert.equal(request.signal, signal); throw new DOMException('Canceled', 'AbortError'); };
  await assert.rejects(api('/api/courses', { signal }), { name: 'AbortError' });
});

test('network and non-JSON proxy errors produce useful messages without stack payload', async () => {
  globalThis.fetch = async () => { throw new Error('socket secret detail'); };
  await assert.rejects(api('/api/courses'), error => error.status === 0 && !error.message.includes('secret'));
  globalThis.fetch = async () => new Response('<html>Bad gateway</html>', { status: 502 });
  await assert.rejects(api('/api/courses'), error => error.status === 502 && !error.message.includes('<html>'));
});

test('invalid success JSON is rejected clearly', async () => {
  globalThis.fetch = async () => new Response('<html>Wrong server</html>', { status: 200 });
  await assert.rejects(api('/api/courses'), error => error instanceof ApiError && /không hợp lệ/.test(error.message));
});

test('expiry handles backend UTC value, legacy JWT, and malformed persisted sessions', () => {
  assert.equal(sessionExpiry({ expiresAtUtc: '2026-10-23T12:00:00Z' }), Date.parse('2026-10-23T12:00:00Z'));
  const payload = Buffer.from(JSON.stringify({ exp: 123456 })).toString('base64url');
  assert.equal(sessionExpiry({ accessToken: `header.${payload}.signature` }), 123456000);
  for (const value of [null, {}, { accessToken: 'broken' }, { expiresAtUtc: 'bad-date' }, { accessToken: 'a.e30.b' }]) assert.equal(sessionExpiry(value), 0);
});

test('quiz payload contains valid option references and never score/correctness', () => {
  const quiz = { id: 1, questions: [{ id: 11, options: [{ id: 21 }, { id: 22 }] }, { id: 12, options: [{ id: 31 }] }] };
  assert.deepEqual(submissionAnswers(quiz, { 11: '22', 12: 31, score: 10, isCorrect: true }), [{ questionId: 11, selectedOptionId: 22 }, { questionId: 12, selectedOptionId: 31 }]);
  assert.throws(() => submissionAnswers(quiz, { 11: 22 }), /tất cả/);
  assert.throws(() => submissionAnswers(quiz, { 11: 999, 12: 31 }), /tất cả/);
  assert.throws(() => submissionAnswers({ questions: [] }, {}), /tất cả/);
});

test('client validation rejects blank answer variants, invalid accounts and oversized uploads', () => {
  assert.equal(validateQuestion({ content: 'Câu hỏi?', options: ['A', 'B', 'C', 'D'], correct: 1 }), '');
  assert.notEqual(validateQuestion({ content: ' ', options: ['A', 'B', 'C', 'D'], correct: 1 }), '');
  assert.notEqual(validateQuestion({ content: 'Câu hỏi?', options: ['A', ' a ', 'C', 'D'], correct: 1 }), '');
  assert.notEqual(validateQuestion({ content: 'Câu hỏi?', options: ['A', 'B', 'C', 'D'], correct: -1 }), '');
  assert.equal(validateAccount({ username: 'student1', fullName: 'Sinh viên', password: 'fixture123' }), '');
  assert.notEqual(validateAccount({ username: '<script>', fullName: 'Sinh viên', password: 'fixture123' }), '');
  assert.equal(validateUpload({ name: 'Unicode.TXT', size: 5000000 }), '');
  assert.notEqual(validateUpload({ name: 'material.txt', size: 5000001 }), '');
  assert.notEqual(validateUpload({ name: 'payload.html', size: 100 }), '');
  assert.notEqual(validateUpload({ name: 'material.txt', size: 0 }), '');
});

test('profile accepts a missing legacy email and validates editable fields', () => {
  assert.equal(validateProfile({ fullName: 'Sinh viên', email: '' }), '');
  assert.equal(validateProfile({ fullName: ' Sinh viên ', email: ' student@example.org ' }), '');
  assert.notEqual(validateProfile({ fullName: ' ', email: '' }), '');
  assert.notEqual(validateProfile({ fullName: 'Sinh viên', email: 'not-an-email' }), '');
  assert.notEqual(validateProfile({ fullName: 'Sinh viên', email: `${'x'.repeat(250)}@example.org` }), '');
});

test('five material formats honor the server upload policy and reject legacy binary formats', () => {
  for (const extension of ['txt', 'PDF', 'docx', 'PPTX', 'md']) assert.equal(validateUpload({ name: `material.${extension}`, size: 100 }), '');
  for (const extension of ['doc', 'ppt', 'docm', 'pptm', 'exe']) assert.notEqual(validateUpload({ name: `material.${extension}`, size: 100 }), '');
  assert.notEqual(validateUpload({ name: 'material.txt', size: 1025 }, 1024, ['.txt']), '');
  assert.notEqual(validateUpload({ name: 'material.pdf', size: 100 }, 1024, ['.txt']), '');
});

test('Office binary previews require the extraction API and never decode zip bytes as text', async () => {
  globalThis.fetch = async () => new Response(new Uint8Array([80, 75, 3, 4]), { headers: { 'Content-Type': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' } });
  await assert.rejects(readMaterial(7, 'token'), error => error instanceof ApiError && /trích xuất/.test(error.message));
});

test('temporary service errors retain Retry-After guidance without automatic client retries', async () => {
  let requests = 0;
  globalThis.fetch = async () => { requests++; return new Response(JSON.stringify({ error: 'Gemini đang quá tải.' }), { status: 503, headers: { 'Retry-After': '12' } }); };
  await assert.rejects(api('/api/ai/questions/generate', { method: 'POST', body: { materialId: 1, count: 3, difficulty: 'Medium' } }), error => error.status === 503 && error.retryAfterSeconds === 12 && /12 giây/.test(error.message));
  assert.equal(requests, 1);
  assert.equal(retryAfterSeconds('Thu, 29 Oct 2026 12:00:05 GMT', Date.parse('2026-10-29T12:00:00Z')), 5);
  assert.equal(retryAfterSeconds('Wed, 28 Oct 2026 12:00:00 GMT', Date.parse('2026-10-29T12:00:00Z')), 0);
  assert.equal(retryAfterSeconds('invalid'), null);
});

test('bank combines topic, status, source and difficulty without assigning a difficulty to legacy questions', () => {
  const questions = [
    { id: 1, topicId: 4, content: 'Tính TỔNG hai số', source: 'AI', status: 'Draft', difficulty: 'Easy' },
    { id: 2, topicId: 4, content: 'Tính tổng hai số', source: 'Manual', status: 'Approved', difficulty: null },
    { id: 3, topicId: 5, content: 'Tính tổng hai số', source: 'AI', status: 'Draft', difficulty: 'Hard' },
    { id: 4, topicId: 4, content: 'Định nghĩa tập hợp', source: 'AI', status: 'Draft', difficulty: 'Easy' },
  ];
  assert.deepEqual(filterQuestions(questions, { search: '  tính tổng ', topicId: '4', status: 'Draft', source: 'AI', difficulty: 'Easy' }).map(item => item.id), [1]);
  assert.deepEqual(filterQuestions(questions, { difficulty: 'Unspecified' }).map(item => item.id), [2]);
  assert.deepEqual(filterQuestions(questions, { topicId: '4', difficulty: 'Hard' }), []);
  assert.equal(filterQuestions(questions).length, 4);
  assert.equal(questions[1].difficulty, null);
});

test('dashboard averages preserve a real zero and clearly label missing submissions', () => {
  assert.equal(formatScore(null), 'Chưa có bài nộp');
  assert.equal(formatScore(undefined), 'Chưa có bài nộp');
  assert.equal(formatScore(0), '0/10');
  assert.equal(formatScore(6.666666), '6,67/10');
});

test('manual questions allow unknown difficulty while rejecting unsupported difficulty values', () => {
  const question = { ...emptyQuestion(), content: 'Câu hỏi', options: ['A', 'B', 'C', 'D'] };
  assert.equal(question.difficulty, '');
  assert.equal(validateQuestion(question), '');
  assert.equal(validateQuestion({ ...question, difficulty: 'Hard' }), '');
  assert.match(validateQuestion({ ...question, difficulty: 'Extreme' }), /độ khó/);
});

test('UTC API dates keep the same instant when EF omits the timezone and preserve explicit offsets', () => {
  const expected = Date.parse('2026-10-29T08:15:30.123Z');
  assert.equal(apiTimestamp('2026-10-29T08:15:30.123'), expected);
  assert.equal(apiTimestamp('2026-10-29T08:15:30.1230000'), expected);
  assert.equal(apiTimestamp('2026-10-29T15:15:30.123+07:00'), expected);
  assert.equal(apiTimestamp('2026-10-29T15:15:30.123+0700'), expected);
  assert.equal(formatTime('2026-10-29T08:15:30.123'), formatTime('2026-10-29T08:15:30.123Z'));
  assert.equal(sessionExpiry({ expiresAtUtc: '2026-10-29T08:15:30.123' }), expected);
  assert.equal(sessionExpiry({ expiresAtUtc: 'invalid' }), 0);
  assert.equal(formatTime(null), '—');
  assert.equal(formatTime('invalid'), '—');
});

test('downloads retain Unicode extensions from server headers and discard directory components', () => {
  assert.equal(contentDispositionFilename("attachment; filename=lesson.pdf; filename*=UTF-8''B%C3%A0i%20gi%E1%BA%A3ng.pdf"), 'Bài giảng.pdf');
  assert.equal(contentDispositionFilename('attachment; filename="lesson; notes.docx"'), 'lesson; notes.docx');
  assert.equal(contentDispositionFilename('attachment; filename="../../lecture.pptx"'), 'lecture.pptx');
  assert.equal(contentDispositionFilename('attachment; filename="C:\\fakepath\\lecture.md"'), 'lecture.md');
  assert.equal(contentDispositionFilename("attachment; filename=fallback.txt; filename*=UTF-8''bad%ZZ"), 'fallback.txt');
  assert.equal(contentDispositionFilename('attachment; filename=".."'), null);
  assert.equal(contentDispositionFilename(null), null);
});

test('authenticated recommendation downloads use original filename rather than a title without extension', async () => {
  const previousDocument = globalThis.document, previousCreate = URL.createObjectURL, previousRevoke = URL.revokeObjectURL, previousTimeout = globalThis.setTimeout;
  let request, clicked = false, removed = false, revoked = '';
  const link = { click: () => { clicked = true; }, remove: () => { removed = true; } };
  try {
    globalThis.fetch = async (url, options) => { request = { url, ...options }; return new Response('document', { headers: { 'Content-Disposition': 'attachment; filename="original.docx"' } }); };
    globalThis.document = { createElement: () => link, body: { appendChild: value => assert.equal(value, link) } };
    URL.createObjectURL = () => 'blob:fixture'; URL.revokeObjectURL = value => { revoked = value; };
    globalThis.setTimeout = callback => { callback(); return 0; };
    await downloadMaterial(7, 'student-token', 'Tiêu đề học liệu');
    assert.equal(request.url, `${API_BASE}/api/materials/7/download`);
    assert.equal(request.headers.Authorization, 'Bearer student-token');
    assert.equal(link.download, 'original.docx'); assert.equal(link.href, 'blob:fixture');
    assert.equal(clicked, true); assert.equal(removed, true); assert.equal(revoked, 'blob:fixture');
  } finally {
    globalThis.document = previousDocument; URL.createObjectURL = previousCreate; URL.revokeObjectURL = previousRevoke; globalThis.setTimeout = previousTimeout;
  }
});
