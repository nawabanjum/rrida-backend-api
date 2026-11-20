-- =============================================================================
-- 02_reference_data.sql — lookup/reference rows a new environment needs
--
-- Applied by the deploy pipeline right after 01_schema.sql, only to an empty
-- database. Contains NO tenant or personal data: no profiles, users, teams,
-- licences, feedback or logs. Included:
--   __EFMigrationsHistory (so the EF bundle only applies newer migrations),
--   user groups/rights, plans, daily + team question sets, reasons, micro
--   actions, prompt templates, model configuration (API keys removed) and
--   other lookup tables.
--
-- ModelConfiguration.ApiKey is stripped. Set real keys per environment in the
-- app if the LLM features are used there.
-- =============================================================================

--
-- PostgreSQL database dump
--

-- Dumped from database version 16.15

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Data for Name: AIModelConfigs; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."AIModelConfigs" ("Id", "PromptName", "ModelName", "Provider", "EndpointUrl", "Role", "Priority", "Strategy", "IsActive", "IsDel", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: PromptTypes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PromptTypes" ("PromptTypeId", "PromptTypeTitle", "UnitId", "OrganizationId") FROM stdin;
1	Delete	4	8
2	View	3	7
3	Edit	2	6
4	Add	1	5
\.


--
-- Data for Name: SystemModules; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."SystemModules" ("ModuleID", "ModuleName", "FriendlyName", "Description") FROM stdin;
1	UserManagement	User Management	\N
2	SystemSetting	System Setting	\N
\.


--
-- Data for Name: UserGroups; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserGroups" ("Id", "Name", "IsAdmin", "UnitId", "OrganizationId", "CreatedBy", "Description") FROM stdin;
1	Profile Admin	t	1	\N	\N	Administers one profile, and the Managers inside it.
2	Team Manager	f	2	\N	\N	Runs the teams assigned to them, inside one profile.
3	RRIDA Admin	t	3	\N	\N	Platform super admin. Sees and manages every profile.
4	Portfolio Manager	f	4	\N	\N	Additional role a Profile Admin may assign. No data access yet.
\.


--
-- Data for Name: AssignUserRights; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."AssignUserRights" ("AssignRightId", "UserGroupId", "SystemModuleId", "PromptTypeId") FROM stdin;
\.


--
-- Data for Name: ContentCultures; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ContentCultures" ("Id", "CultureName") FROM stdin;
1	en-US
\.


--
-- Data for Name: CurrentRelationshipStatuses; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."CurrentRelationshipStatuses" ("Id", "Name") FROM stdin;
1	Existing client/relationship
2	Referral from existing network
3	Cold outreach/prospecting
4	Inbound lead
\.


--
-- Data for Name: DailyQuestionSetGroups; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DailyQuestionSetGroups" ("Id", "DailyQuestionSetType") FROM stdin;
1	1
\.


--
-- Data for Name: DailyQuestionSets; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DailyQuestionSets" ("Id", "VersionNumber", "Name", "ContentCultureId", "DailyQuestionSetGroupId") FROM stdin;
1	1	Employee Engagement Question Set	1	1
\.


--
-- Data for Name: DailyQuestions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DailyQuestions" ("Id", "Text", "ContentCultureId") FROM stdin;
1	Did you feel understood by others today?	1
2	Did today's challenges enhance your personal growth?	1
3	Were you able to seek new learning opportunities today?	1
4	Were you comfortable in group settings today?	1
5	Did the tools you use enhance your efficiency and satisfaction today?	1
6	Did you allocate time for self-reflection on your tasks today?	1
7	Were you comfortable sharing your thoughts and feedback today?	1
8	Were you equipped and supported in learning new tasks today?	1
9	Were you encouraged to explore beyond your comfort zone today?	1
10	Were you supported by your team today?	1
11	Were your methods for completing tasks personally satisfying today?	1
12	Did reflecting on outcomes contribute to your growth today?	1
13	Did you find clarity in your communications today?	1
14	Were you able to face and admit your mistakes today?	1
15	Did you feel your efforts were acknowledged today?	1
16	Could you express your frustrations freely today?	1
17	Did you feel autonomous in managing your work today?	1
18	Did you receive constructive feedback today?	1
19	How productive and respectful were your interactions today?	1
20	Do you feel invested in your own development today?	1
21	How easy was it to collaborate and connect with others today?	1
22	Were you or others able to easily talk through failures today?	1
23	How accessible was support when you faced difficulties today?	1
24	Did you celebrate a success today?	1
25	Was seeking help emotionally comfortable for you today?	1
26	Did you receive feedback that fueled your growth today?	1
27	Is your career path aligning with your personal goals?	1
28	Were you able to meaningfully engage with someone today?	1
29	Did your workload allow for a focus on quality and well-being today?	1
30	Did you recognize your strengths and areas for improvement today?	1
55	Do you feel your voice is heard at your workplace?	1
56	Are you able to find new learning opportunities?	1
57	Have you been encouraged to work outside your comfort zone recently?	1
58	Are you able to meaningfully engage with your colleagues	1
59	Do you feel autonomous in managing your work?	1
60	Have you spent time reflecting on your strengths and areas for improvement recently?	1
61	Do you feel well-informed about organisational decisions that affect your work?	1
62	Have you learned something new through your work recently?	1
63	Do you feel your work contributes to something meaningful?	1
64	Do you feel safe admitting when you need help?	1
65	Do you have what you need to do your job effectively?	1
66	Have you discussed a persistent challenge with you manager lately?	1
67	Are your expected objectives and deliverables clear to you?	1
68	Are you encouraged to develop new skills?	1
69	Are you given opportunities to take on new challenges?	1
70	Can you be honest about challenges you're facing?	1
71	Are processes helping rather than hindering your work?	1
72	Do you regularly review what is working well?	1
73	Do you receive timely updates that help you do your job well?	1
74	Do your tasks stretch your capabilities in a positive way?	1
75	Do you see a future for yourself in this organisation?	1
76	Do you feel accepted for who you are at work?	1
77	Do tools support you in being productive?	1
78	Do you regularly reflect and plan activites to improve your ways of working?	1
79	Do you receive feedback in a clear and constructive way?	1
80	Do you receive guidance that helps you improve?	1
81	Are your strengths used effectively in your role?	1
82	Is it safe to speak up when something isn't working?	1
83	Is your workload manageable?	1
84	Do you pause to acknowledge progress you've made?	1
85	Do meetings with your management result in clear outcomes and next steps?	1
86	Are learning resources easy to access?	1
87	Do you have opportunities to grow your responsibilities?	1
88	Do leaders respond well to openness and honesty?	1
89	Can you prioritise your work effectively?	1
90	Are you able to apply learnings from recent successes and failures?	1
91	During meetings is everyone given an opportunity to speak and share their views?	1
92	Do mistakes or a need for help lead to learning opportunities for you?	1
93	Are your ideas given space to be explored?	1
94	Do you feel comfortable sharing concerns with your team?	1
95	Do systems enable you to move work forward smoothly?	1
96	Is reflection encouraged by your manager or team?	1
97	Are changes communicated early enough to prepare?	1
98	Do you feel supported in building new expertise?	1
99	Do you feel trusted with important work?	1
100	Is empathy shown when difficulties arise?	1
101	Are dependencies or blockers handled efficiently?	1
102	Is reflecting on your work a regular activity for you and your team?	1
103	Do leaders explain the reasoning behind key decisions?	1
104	Is learning embedded in your day-to-day work?	1
105	Are you exposed to opportunities beyond your immediate role?	1
106	Do you feel supported during setbacks?	1
107	Do you have enough focus time to do meaningful work?	1
109	Do you receive recognition for your work?	1
110	Do you have time to reflect on what you've learned?	1
111	Do you feel your contributions make an impact?	1
112	Can you express uncertainty without judgement?	1
113	Are obstacles addressed quickly when they arise?	1
114	Do you discuss reflection about your work and working environment with colleagues?	1
115	Do you feel listened to during discussions?	1
116	Do you feel your skills are progressing in your current role?	1
117	Do you feel encouraged to pursue new paths internally?	1
119	Do ways of working support high-quality outcomes?	1
120	Are you able to act on your reflections about how to improve at work?	1
46	Are you able to easily talk through your mistakes or failures?	1
108	Have you discussed how to improve you ways of working with your manager or team recently?	1
121	When plans change do you hear about it early enough to adjust?	1
122	Is your work stretching you or have you started coasting?	1
123	Does your work feel like it has real purpose right now?	1
124	When things get hard does your team actually have your back?	1
125	How productive have your last few days really been?	1
126	Have you paused to reflect on your work this past week?	1
127	Could you clearly explain what your team is working toward right now?	1
128	Have you put real time into your own growth lately?	1
129	Does your daily work line up with where you want your career to go?	1
130	Can you share what you really think at work?	1
131	How easily can you work with people outside your team?	1
132	Has your manager given you feedback worth reflecting on lately?	1
133	When you speak up do people really take your input on board?	1
134	Has recent feedback actually fuelled your growth?	1
135	Have you talked about your next career step with your manager lately?	1
136	Can you talk openly about your mistakes without fear?	1
137	Do the ways you get work done actually feel good?	1
138	Does your workload leave you any room to think?	1
139	Has your work been noticed and acknowledged in the last few days?	1
140	Did your last training give you something you still use?	1
141	Do you feel supported to go for internal opportunities?	1
142	Do you feel genuinely understood by the people you work with?	1
143	Are your tools helping you work or getting in the way?	1
31	Have your colleagues been respectful to you lately?	1
32	Are your tasks and challenges contributing to your personal growth?	1
33	Are you feeling purpose in your work?	1
34	Do you feel supported by your team?	1
35	How productive have your last few days been?	1
36	Have you taken time to reflect on your work in the last week?	1
37	Is messaging from management clear and understandable?	1
38	Have you invested time into your personal development recently?	1
39	Does your daily work align with your career goals?	1
40	Are you comfortable sharing your true thoughts and feedback at work?	1
41	How easy is it to collaborate with others outside of your team?	1
42	Have you recently received constructive feedback from your manager?	1
43	Do you feel your inputs are understood by others?	1
44	Have you received constructive feedback that's fuelled your growth lately?	1
45	Have you discussed your next growth or career step with your manager recently?	1
47	Are the available methods for completing tasks personally satisfying?	1
48	Do you feel your workload is healthy?	1
49	Have your efforts been acknowledged in the last few days?	1
50	How do you feel about your last training sponsored by your orgainsation?	1
51	Are you supported in applying for internal opportunties?	1
52	Did you feel understood by others at work?	1
53	Are the tools you use helping you be efficient and effective?	1
54	Have you celebrated a success in the past week?	1
144	Have you celebrated a win in the past week?	1
145	Be honest: does speaking up here actually change anything?	1
146	Can you easily find new things to learn here?	1
147	Have you been nudged outside your comfort zone recently?	1
148	Do you connect with colleagues in a way that feels real?	1
149	Do you have real freedom over how you manage your work?	1
150	Have you thought about your strengths and gaps recently?	1
151	Do you understand the real reasons behind decisions that affect you?	1
152	Have you learned something genuinely new through work this week?	1
153	Does your work feel like it adds up to something meaningful?	1
154	Is it safe to admit when you need help?	1
155	Do you have what you need to do your job well?	1
156	Have you raised a recurring challenge with your manager lately?	1
157	Is it clear exactly what's expected of you this week?	1
158	Do you feel encouraged to build new skills?	1
160	Can you be honest about the challenges you're facing?	1
162	Do you regularly take stock of what's working?	1
164	Do your tasks stretch you in a good way?	1
166	Do you feel accepted for who you really are at work?	1
168	Do you plan small changes to improve how you work?	1
170	Does the guidance you get actually help you improve?	1
172	Is it safe to flag when something isn't working?	1
174	Do you pause to recognise the progress you've made?	1
176	Are the resources you need to learn easy to reach?	1
178	Do leaders respond well when people are open and honest?	1
180	Are you turning recent wins and misses into lessons?	1
182	When you struggle does it turn into real learning?	1
184	Can you raise concerns with your team without holding back?	1
186	Does your manager or team make space for reflection?	1
188	Do you feel backed when building new expertise?	1
190	When things go wrong is empathy shown?	1
192	Is reflecting on your work a regular habit for your team?	1
194	Is learning part of your normal day rather than an afterthought?	1
196	Do you feel supported when you hit a setback?	1
198	Have you discussed improving how you work with your team lately?	1
200	Do you get time to absorb what you've learned?	1
202	Can you admit you're unsure without being judged?	1
204	Do you talk through reflections with your colleagues?	1
206	Honestly are your skills moving forward in this role?	1
159	Are you getting real chances to take on new challenges?	1
161	Are your processes helping you or slowing you down?	1
163	Do you get the information you need in time to act on it?	1
165	Can you picture a future for yourself here?	1
167	Do your tools genuinely make you more productive?	1
169	Is the feedback you get clear enough to actually use?	1
171	Are your real strengths being put to good use?	1
173	Is your workload actually manageable right now?	1
175	Do meetings with your manager leave you clear on what's next?	1
177	Do you have room to grow what you're responsible for?	1
179	Can you prioritise your work without it falling apart?	1
181	In team meetings does everyone genuinely get a chance to speak?	1
183	Do your ideas get space to actually be explored?	1
185	Do your systems let you move work forward smoothly?	1
187	Are changes shared early enough for you to prepare?	1
189	Do you feel trusted with work that matters?	1
191	Are blockers cleared quickly when they come up?	1
193	Do leaders share the thinking behind decisions and not just the outcome?	1
195	Do you see opportunities beyond your current role?	1
197	Do you get enough focus time for work that matters?	1
199	Do you feel genuinely recognised for the work you do?	1
201	Does the work you do feel like it makes an impact?	1
203	Are obstacles dealt with fast when they appear?	1
205	In conversations do you feel truly listened to?	1
207	Do you feel encouraged to explore new paths inside the company?	1
118	Do you feel emotionally safe at work?	1
208	Do you feel emotionally safe at work?	1
209	Do your ways of working help you do great work?	1
210	Do you actually act on what your reflections tell you?	1
\.


--
-- Data for Name: DailyQuestionSpecifications; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DailyQuestionSpecifications" ("Id", "DailyQuestionSetGroupId", "DailyQuestionSetId", "CloverCategory", "Ordinal", "ContentCultureId", "QuestionId") FROM stdin;
1	1	1	1	1	1	1
2	1	1	2	2	1	2
3	1	1	3	3	1	3
4	1	1	4	4	1	4
5	1	1	5	5	1	5
6	1	1	6	6	1	6
7	1	1	1	7	1	7
8	1	1	2	8	1	8
9	1	1	3	9	1	9
10	1	1	4	10	1	10
11	1	1	5	11	1	11
12	1	1	6	12	1	12
13	1	1	1	13	1	13
14	1	1	2	14	1	14
15	1	1	3	15	1	15
16	1	1	4	16	1	16
17	1	1	5	17	1	17
18	1	1	6	18	1	18
19	1	1	1	19	1	19
20	1	1	2	20	1	20
21	1	1	3	21	1	21
22	1	1	4	22	1	22
23	1	1	5	23	1	23
24	1	1	6	24	1	24
25	1	1	1	25	1	25
26	1	1	2	26	1	26
27	1	1	3	27	1	27
28	1	1	4	28	1	28
29	1	1	5	29	1	29
30	1	1	6	30	1	30
54	1	1	6	54	1	54
55	1	1	1	55	1	55
56	1	1	2	56	1	56
57	1	1	3	57	1	57
58	1	1	4	58	1	58
59	1	1	5	59	1	59
60	1	1	6	60	1	60
61	1	1	1	61	1	61
62	1	1	2	62	1	62
63	1	1	3	63	1	63
64	1	1	4	64	1	64
65	1	1	5	65	1	65
66	1	1	6	66	1	66
67	1	1	1	67	1	67
68	1	1	2	68	1	68
69	1	1	3	69	1	69
70	1	1	4	70	1	70
71	1	1	5	71	1	71
72	1	1	6	72	1	72
73	1	1	1	73	1	73
74	1	1	2	74	1	74
75	1	1	3	75	1	75
76	1	1	4	76	1	76
77	1	1	5	77	1	77
78	1	1	6	78	1	78
79	1	1	1	79	1	79
80	1	1	2	80	1	80
81	1	1	3	81	1	81
82	1	1	4	82	1	82
83	1	1	5	83	1	83
84	1	1	6	84	1	84
85	1	1	1	85	1	85
86	1	1	2	86	1	86
87	1	1	3	87	1	87
88	1	1	4	88	1	88
89	1	1	5	89	1	89
90	1	1	6	90	1	90
91	1	1	1	91	1	91
92	1	1	2	92	1	92
93	1	1	3	93	1	93
94	1	1	4	94	1	94
95	1	1	5	95	1	95
96	1	1	6	96	1	96
97	1	1	1	97	1	97
98	1	1	2	98	1	98
99	1	1	3	99	1	99
100	1	1	4	100	1	100
101	1	1	5	101	1	101
102	1	1	6	102	1	102
103	1	1	1	103	1	103
104	1	1	2	104	1	104
105	1	1	3	105	1	105
106	1	1	4	106	1	106
107	1	1	5	107	1	107
108	1	1	6	108	1	108
109	1	1	1	109	1	109
110	1	1	2	110	1	110
111	1	1	3	111	1	111
112	1	1	4	112	1	112
113	1	1	5	113	1	113
114	1	1	6	114	1	114
115	1	1	1	115	1	115
116	1	1	2	116	1	116
117	1	1	3	117	1	117
119	1	1	5	119	1	119
120	1	1	6	120	1	120
122	1	1	2	122	1	122
124	1	1	4	124	1	124
126	1	1	6	126	1	126
128	1	1	2	128	1	128
130	1	1	4	130	1	130
132	1	1	6	132	1	132
134	1	1	2	134	1	134
136	1	1	4	136	1	136
138	1	1	6	138	1	138
140	1	1	2	140	1	140
121	1	1	1	121	1	121
123	1	1	3	123	1	123
125	1	1	5	125	1	125
127	1	1	1	127	1	127
129	1	1	3	129	1	129
131	1	1	5	131	1	131
133	1	1	1	133	1	133
135	1	1	3	135	1	135
137	1	1	5	137	1	137
139	1	1	1	139	1	139
141	1	1	3	141	1	141
142	1	1	4	142	1	142
143	1	1	5	143	1	143
31	1	1	1	31	1	31
32	1	1	2	32	1	32
33	1	1	3	33	1	33
34	1	1	4	34	1	34
35	1	1	5	35	1	35
36	1	1	6	36	1	36
37	1	1	1	37	1	37
38	1	1	2	38	1	38
39	1	1	3	39	1	39
40	1	1	4	40	1	40
41	1	1	5	41	1	41
42	1	1	6	42	1	42
43	1	1	1	43	1	43
44	1	1	2	44	1	44
45	1	1	3	45	1	45
46	1	1	4	46	1	46
47	1	1	5	47	1	47
48	1	1	6	48	1	48
49	1	1	1	49	1	49
50	1	1	2	50	1	50
51	1	1	3	51	1	51
52	1	1	4	52	1	52
53	1	1	5	53	1	53
144	1	1	6	144	1	144
145	1	1	1	145	1	145
146	1	1	2	146	1	146
147	1	1	3	147	1	147
148	1	1	4	148	1	148
149	1	1	5	149	1	149
150	1	1	6	150	1	150
151	1	1	1	151	1	151
152	1	1	2	152	1	152
153	1	1	3	153	1	153
154	1	1	4	154	1	154
155	1	1	5	155	1	155
156	1	1	6	156	1	156
157	1	1	1	157	1	157
158	1	1	2	158	1	158
159	1	1	3	159	1	159
160	1	1	4	160	1	160
161	1	1	5	161	1	161
162	1	1	6	162	1	162
163	1	1	1	163	1	163
164	1	1	2	164	1	164
165	1	1	3	165	1	165
166	1	1	4	166	1	166
167	1	1	5	167	1	167
168	1	1	6	168	1	168
169	1	1	1	169	1	169
170	1	1	2	170	1	170
171	1	1	3	171	1	171
172	1	1	4	172	1	172
173	1	1	5	173	1	173
174	1	1	6	174	1	174
175	1	1	1	175	1	175
176	1	1	2	176	1	176
177	1	1	3	177	1	177
178	1	1	4	178	1	178
179	1	1	5	179	1	179
180	1	1	6	180	1	180
181	1	1	1	181	1	181
182	1	1	2	182	1	182
183	1	1	3	183	1	183
184	1	1	4	184	1	184
185	1	1	5	185	1	185
186	1	1	6	186	1	186
187	1	1	1	187	1	187
188	1	1	2	188	1	188
189	1	1	3	189	1	189
190	1	1	4	190	1	190
191	1	1	5	191	1	191
192	1	1	6	192	1	192
193	1	1	1	193	1	193
194	1	1	2	194	1	194
195	1	1	3	195	1	195
196	1	1	4	196	1	196
197	1	1	5	197	1	197
198	1	1	6	198	1	198
199	1	1	1	199	1	199
200	1	1	2	200	1	200
201	1	1	3	201	1	201
202	1	1	4	202	1	202
203	1	1	5	203	1	203
204	1	1	6	204	1	204
205	1	1	1	205	1	205
206	1	1	2	206	1	206
207	1	1	3	207	1	207
118	1	1	4	118	1	118
208	1	1	4	208	1	208
209	1	1	5	209	1	209
210	1	1	6	210	1	210
\.


--
-- Data for Name: DailyReasons; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DailyReasons" ("Id", "DailyReasonSentimentType", "Text", "RequiresFreeTextFeedback", "SpecificationId", "ContentCultureId") FROM stdin;
1	2	Others seemed distracted or disinterested in my input.	f	1	1
2	2	My points were misinterpreted or overlooked.	f	1	1
3	2	There was a lack of effort to clarify misunderstandings.	f	1	1
4	1	Felt actively listened to and acknowledged.	f	1	1
5	1	My input was valued and prompted further discussion.	f	1	1
6	1	There was a mutual understanding in all conversations.	f	1	1
7	2	Challenges were routine, offered no new learning.	f	2	1
8	2	Felt overwhelmed, unable to focus on growth.	f	2	1
9	2	Lack of reflection on challenges to glean insights.	f	2	1
10	1	Overcame complex problems that improved skills.	f	2	1
11	1	Learned valuable lessons from today's tasks.	f	2	1
12	1	Challenges matched my growth goals and aspirations.	f	2	1
13	2	No new opportunities were present or accessible.	f	3	1
14	2	Current workload prevented exploration of learning.	f	3	1
15	2	Learning opportunities were not communicated.	f	3	1
16	1	Actively engaged in new learning experiences.	f	3	1
17	1	Resources were available and accessible for learning.	f	3	1
18	1	Encouragement from leadership to pursue learning.	f	3	1
19	2	Group dynamics felt exclusive or unwelcoming.	f	4	1
20	2	Felt anxious or unprepared for group interaction.	f	4	1
21	2	Group settings were not conducive to open dialogue.	f	4	1
22	1	Felt included and valued in group discussions.	f	4	1
23	1	Group atmosphere was collaborative and friendly.	f	4	1
24	1	There was clear and respectful communication in the group.	f	4	1
25	2	Tools were outdated or malfunctioning.	f	5	1
26	2	Lack of training on tool usage hindered efficiency.	f	5	1
27	2	Tools didn't integrate well with other systems.	f	5	1
28	1	Tools were up-to-date and user-friendly.	f	5	1
29	1	Tools improved task completion speed.	f	5	1
30	1	High satisfaction due to seamless technology experience.	f	5	1
31	2	Overwhelmed with tasks, no time for reflection.	f	6	1
32	2	Self-reflection not seen as a priority in daily routine.	f	6	1
33	2	Unsure how to effectively self-reflect.	f	6	1
34	1	Intentionally scheduled reflection time.	f	6	1
35	1	Recognized the value of reflection in improving performance.	f	6	1
36	1	Have effective methods for brief, daily reflection.	f	6	1
37	2	I hesitated due to fear of negative repercussions.	f	7	1
38	2	The environment didn't feel open or receptive to feedback.	f	7	1
39	2	Past experiences have made me wary of sharing.	f	7	1
40	1	I felt confident and safe to express my views.	f	7	1
41	1	The culture encouraged open and honest dialogue.	f	7	1
42	1	Received positive reinforcement when providing feedback.	f	7	1
43	2	Resources were insufficient for new task requirements.	f	8	1
44	2	Support from peers or superiors was lacking.	f	8	1
45	2	Felt unprepared to tackle new tasks effectively.	f	8	1
46	1	Had the tools and guidance needed for new tasks.	f	8	1
47	1	Supportive environment made learning new tasks easier.	f	8	1
48	1	Training materials were clear and easily accessible.	f	8	1
49	2	Environment felt risk-averse and discouraging.	f	9	1
50	2	No explicit encouragement or support for risk-taking.	f	9	1
51	2	Concerns about failure limited exploration.	f	9	1
52	1	Received positive reinforcement for taking risks.	f	9	1
53	1	Team culture supported experimenting and learning.	f	9	1
54	1	Management provided opportunities for growth.	f	9	1
55	2	Team was unavailable or indifferent to my needs.	f	10	1
56	2	Support offered was ineffective or irrelevant.	f	10	1
57	2	Team environment felt competitive, not supportive.	f	10	1
58	1	Team provided timely and helpful support.	f	10	1
59	1	Felt a strong sense of camaraderie and backing.	f	10	1
60	1	Team actively offered assistance before I had to ask.	f	10	1
61	2	Methods were imposed rather than chosen, felt restrictive.	f	11	1
62	2	Task methods were inefficient, causing frustration.	f	11	1
63	2	Methods didn't allow for personal work style.	f	11	1
64	1	Methods aligned with personal preferences and strengths.	f	11	1
65	1	Achieved a sense of accomplishment using chosen methods.	f	11	1
66	1	Methods allowed for creativity and innovation.	f	11	1
67	2	Outcomes were not reviewed or discussed today.	f	12	1
68	2	Lack of understanding on how to learn from outcomes.	f	12	1
69	2	Reflection felt unproductive or unclear.	f	12	1
70	1	Reflection provided insights for improvement.	f	12	1
71	1	Linked today's outcomes to long-term goals.	f	12	1
72	1	Encouraged by others to reflect on outcomes.	f	12	1
73	2	Messages were ambiguous and led to confusion.	f	13	1
74	2	Lack of direct and clear communication from others.	f	13	1
75	2	Information was fragmented and inconsistent.	f	13	1
76	1	Communications were clear and direct, preventing misunderstandings.	f	13	1
77	1	Received comprehensive information when needed.	f	13	1
78	1	There was a focus on ensuring mutual understanding.	f	13	1
79	2	Concerns about judgment prevented admitting mistakes.	f	14	1
80	2	No opportunity to discuss or learn from mistakes.	f	14	1
81	2	Mistakes were met with negative consequences.	f	14	1
82	1	Openly discussed mistakes and learned from them.	f	14	1
83	1	Mistakes were viewed as part of the learning process.	f	14	1
84	1	Received support in addressing and correcting mistakes.	f	14	1
85	2	Efforts went unnoticed or unappreciated.	f	15	1
86	2	Acknowledgement lacked sincerity or specificity.	f	15	1
87	2	Positive contributions were overshadowed by others.	f	15	1
88	1	Direct recognition for efforts from peers or management.	f	15	1
89	1	Acknowledgement felt meaningful and personalized.	f	15	1
90	1	Contributions led to visible outcomes or praise.	f	15	1
91	2	Concerned about being judged or dismissed for expressing frustration.	f	16	1
92	2	Workplace culture discourages sharing negative emotions.	f	16	1
93	2	Past experiences of negative backlash for speaking out.	f	16	1
94	1	Felt safe to share concerns without judgment.	f	16	1
95	1	Encouragement to voice frustrations constructively.	f	16	1
96	1	Positive mechanisms in place for addressing and managing stress.	f	16	1
97	2	Micromanagement limited decision-making freedom.	f	17	1
98	2	Strict policies restricted how tasks could be approached.	f	17	1
99	2	Felt a lack of trust from superiors in managing work.	f	17	1
100	1	Empowered to make decisions and manage tasks independently.	f	17	1
101	1	Freedom to choose how to approach work.	f	17	1
102	1	Trusted by management to handle work autonomously.	f	17	1
103	2	Feedback was absent or not relevant.	f	18	1
104	2	Constructive feedback overshadowed by criticism.	f	18	1
105	2	Not provided in a timely manner.	f	18	1
106	1	Received specific, actionable suggestions.	f	18	1
107	1	Feedback helped clarify expectations.	f	18	1
108	1	Positive reinforcement included in feedback.	f	18	1
109	2	Encountered dismissive or uncooperative behavior.	f	19	1
110	2	Interactions lacked focus, hindering productivity.	f	19	1
111	2	Felt disrespected or undervalued in exchanges.	f	19	1
112	1	Interactions were constructive and goal-oriented.	f	19	1
113	1	Respect was maintained throughout all interactions.	f	19	1
114	1	Positive engagement led to tangible progress.	f	19	1
115	2	Development felt like a low priority compared to other tasks.	f	20	1
116	2	Lack of personal development opportunities.	f	20	1
117	2	Did not see clear value in available development activities.	f	20	1
118	1	Actively engaged in activities that promote self-improvement.	f	20	1
119	1	Personal development aligned with career goals.	f	20	1
120	1	Felt motivated by visible progress in skills and knowledge.	f	20	1
121	2	Collaboration was hindered by silos or lack of tools.	f	21	1
122	2	Difficult to find opportunities for meaningful connection.	f	21	1
123	2	Remote or distributed work made collaboration challenging.	f	21	1
124	1	Seamless collaboration through effective communication.	f	21	1
125	1	Tools and systems facilitated easy connection.	f	21	1
126	1	Felt a strong sense of community and teamwork.	f	21	1
127	2	Fear of blame or repercussions hindered open discussions.	f	22	1
128	2	Lack of a safe space to discuss and learn from failures.	f	22	1
129	2	Failures are often ignored or stigmatized, not discussed.	f	22	1
130	1	Failures were openly discussed as learning opportunities.	f	22	1
131	1	There was a non-judgmental approach to addressing failures.	f	22	1
132	1	Leadership encouraged transparent dialogue about setbacks.	f	22	1
133	2	Support was hard to reach or unresponsive.	f	23	1
134	2	Assistance was not helpful or relevant to the issues faced.	f	23	1
135	2	Felt isolated when needing help.	f	23	1
136	1	Support was readily available and helpful.	f	23	1
137	1	Received prompt and effective guidance.	f	23	1
138	1	Felt part of a supportive community.	f	23	1
139	2	Successes went unnoticed or were underappreciated.	f	24	1
140	2	No opportunity or platform to share successes.	f	24	1
141	2	Culture doesn't emphasize celebrating small wins.	f	24	1
142	1	Took time to appreciate completed tasks.	f	24	1
143	1	Shared and celebrated with colleagues or team.	f	24	1
144	1	Positive atmosphere around acknowledging achievements.	f	24	1
145	2	Fear of being judged or showing vulnerability.	f	25	1
146	2	Previous requests for help were met with negative attitudes.	f	25	1
147	2	Support systems felt inaccessible or unsympathetic.	f	25	1
148	1	Help was offered in a non-judgmental, supportive manner.	f	25	1
149	1	Felt encouraged and validated when asking for assistance.	f	25	1
150	1	The environment promoted a sense of safety in seeking help.	f	25	1
151	2	Feedback was sparse, not constructive.	f	26	1
152	2	Feedback focused on negatives without guidance.	f	26	1
153	2	Received feedback too late to be actionable.	f	26	1
154	1	Feedback was specific, timely, and helpful.	f	26	1
155	1	Constructive feedback led to immediate improvement.	f	26	1
156	1	Feedback provided new perspectives and learning opportunities.	f	26	1
157	2	Career path feels disconnected from personal aspirations.	f	27	1
158	2	Lack of clarity or support in how to align career and goals.	f	27	1
159	2	Career progression seems limited or off course.	f	27	1
160	1	Career milestones are in sync with personal goals.	f	27	1
161	1	Clear direction and support aligning career and personal goals.	f	27	1
162	1	Opportunities present that resonate with personal ambitions.	f	27	1
163	2	Interactions felt superficial or task-focused only.	f	28	1
164	2	Limited opportunities for deeper, one-on-one engagement.	f	28	1
165	2	Social barriers or time constraints hindered meaningful connection.	f	28	1
166	1	Had a significant conversation that went beyond work.	f	28	1
167	1	Engaged in a personal and insightful dialogue with a colleague.	f	28	1
168	1	Found common ground that deepened a professional relationship.	f	28	1
169	2	High volume of work compromised quality and well-being.	f	29	1
170	2	Pressure to meet deadlines overshadowed well-being.	f	29	1
171	2	Workload left no room for breaks or personal care.	f	29	1
172	1	Workload was balanced, promoting quality output.	f	29	1
173	1	Well-being was considered in task planning.	f	29	1
174	1	Time for self-care and quality focus was ample.	f	29	1
175	2	Focused only on tasks, not on personal development.	f	30	1
176	2	Not accustomed to assessing personal performance daily.	f	30	1
177	2	Difficulty identifying own strengths and weaknesses.	f	30	1
178	1	Self-awareness is a regular part of my routine.	f	30	1
179	1	Received feedback that highlighted strengths and weaknesses.	f	30	1
180	1	Used self-reflection tools to identify areas to work on.	f	30	1
319	1	We're encouraged to share our successes	f	54	1
320	1	I feel it adds positivity to the workplace when I share	f	54	1
321	1	People appreciate when we share successes	f	54	1
322	2	Our culture doesn't emphasize celebrating wins	f	54	1
323	2	I don't want to come across as gloating	f	54	1
324	2	Successes go unnoticed or underappreciated	f	54	1
325	1	My manager takes time to check in with us and ask for our input	f	55	1
326	1	Our culture promotes inclusive and open dialogue	f	55	1
327	1	We have accessible channels to provide feedback	f	55	1
328	2	Everyone is too busy to listen effectively	f	55	1
329	2	No channels for feedback	f	55	1
330	2	I just stay in my lane and do my work	f	55	1
331	1	I am actively engaged in a new learning experience	f	56	1
332	1	Good learning resouces were  accessible	f	56	1
333	1	Leadership encourages time spent learning	f	56	1
334	2	No new opportunities were present or accessible	f	56	1
335	2	Current workload prevented exploration of learning	f	56	1
336	2	Available learning opportunities aren't relevant to my interests	f	56	1
337	1	Team culture supports experimenting and learning	f	57	1
338	1	Management supports reasonble risk taking even when it fails	f	57	1
339	1	I have received positive reinforcement for trying something new	f	57	1
340	2	No explicit encouragement or support for risk-taking	f	57	1
341	2	Too busy with other work	f	57	1
342	2	Environment feels risk-averse	f	57	1
343	1	Had a significant conversation that went beyond work	f	58	1
344	1	I engaged in a personal and insightful dialogue with a colleague	f	58	1
345	1	I found common ground that deepened a professional relationship	f	58	1
346	2	Interactions feel superficial or task-focused only	f	58	1
347	2	Opportunities for deeper one-on-one engagement have been limited	f	58	1
348	2	Social barriers or time constraints hindered meaningful connection	f	58	1
349	1	We're empowered to make decisions and manage tasks independently	f	59	1
350	1	I have the freedom to choose how to approach my work	f	59	1
351	1	I'm trusted by management to handle my work autonomously	f	59	1
352	2	Micromanagement limits decision-making freedom	f	59	1
353	2	Strict policies restrict how tasks can be approached	f	59	1
354	2	Feel a lack of trust from superiors in managing work	f	59	1
355	1	Self-awareness is a regular part of my routine	f	60	1
356	1	I have received feedback that has highlighted my strengths and or weaknesses	f	60	1
357	1	I'm trying to identify my next learning goals	f	60	1
358	2	There is not enough time to reflect	f	60	1
359	2	I find it difficult to identify my strengths and weeknesses	f	60	1
360	2	There is no point as development opportunities aren't available	f	60	1
361	1	I'm kept in the loop early enough to plan and adapt	f	61	1
362	1	The rationale behind decisions is explained clearly	f	61	1
371	2	Work feels repetitive, with few development moments	f	62	1
364	2	Decisions land late or without enough context	f	61	1
365	2	I'm left guessing why changes are being made	f	61	1
366	2	The lack of clarity creates confusion and rework	f	61	1
384	2	I hold back, which limits trust and collaboration	f	64	1
389	2	I'm missing key information, access, or support	f	65	1
396	2	Without reflection time, issues tend to repeat	f	66	1
407	2	Work feels repetitive, with few development moments	f	68	1
428	1	Reflection helps me reset, prioritise, and improve	f	72	1
429	1	Taking stock gives me clarity and direction	f	72	1
432	2	Without reflection, I miss chances to learn and improve	f	72	1
439	1	I'm being stretched in a healthy, supportive way	f	74	1
464	1	Reflection helps me reset, prioritise, and improve	f	78	1
465	1	Taking stock gives me clarity and direction	f	78	1
468	2	Without reflection, I miss chances to learn and improve	f	78	1
469	1	Conversations feel clear, respectful, and two-way	f	79	1
472	2	Messages are unclear, inconsistent, or easy to misread	f	79	1
475	1	I'm being stretched in a healthy, supportive way	f	80	1
492	2	I hold back, which limits trust and collaboration	f	82	1
500	1	Reflection helps me reset, prioritise, and improve	f	84	1
501	1	Taking stock gives me clarity and direction	f	84	1
504	2	Without reflection, I miss chances to learn and improve	f	84	1
505	1	Conversations feel clear, respectful, and two-way	f	85	1
508	2	Messages are unclear, inconsistent, or easy to misread	f	85	1
515	2	Work feels repetitive, with few development moments	f	86	1
536	1	Reflection helps me reset, prioritise, and improve	f	90	1
537	1	Taking stock gives me clarity and direction	f	90	1
540	2	Without reflection, I miss chances to learn and improve	f	90	1
541	1	Conversations feel clear, respectful, and two-way	f	91	1
544	2	Messages are unclear, inconsistent, or easy to misread	f	91	1
551	2	Work feels repetitive, with few development moments	f	92	1
572	1	Reflection helps me reset, prioritise, and improve	f	96	1
573	1	Taking stock gives me clarity and direction	f	96	1
397	1	Expectations are clear and I know what good looks like	f	67	1
398	1	I can confirm priorities and next steps easily	f	67	1
399	1	Clarity helps me deliver confidently	f	67	1
400	2	Expectations feel vague or change without notice	f	67	1
401	2	I'm unsure what success looks like	f	67	1
402	2	Misalignment leads to wasted effort or frustration	f	67	1
576	2	Without reflection, I miss chances to learn and improve	f	96	1
577	1	Conversations feel clear, respectful, and two-way	f	97	1
580	2	Messages are unclear, inconsistent, or easy to misread	f	97	1
587	2	Work feels repetitive, with few development moments	f	98	1
605	2	I'm missing key information, access, or support	f	101	1
608	1	Reflection helps me reset, prioritise, and improve	f	102	1
609	1	Taking stock gives me clarity and direction	f	102	1
612	2	Without reflection, I miss chances to learn and improve	f	102	1
623	2	Work feels repetitive, with few development moments	f	104	1
649	1	Conversations feel clear, respectful, and two-way	f	109	1
652	2	Messages are unclear, inconsistent, or easy to misread	f	109	1
659	2	Work feels repetitive, with few development moments	f	110	1
677	2	I'm missing key information, access, or support	f	113	1
680	1	Reflection helps me reset, prioritise, and improve	f	114	1
681	1	Taking stock gives me clarity and direction	f	114	1
1276	1	Other–I'll expain futher in the text box	t	160	1
1277	2	Sharing struggles feels unsafe here	f	160	1
1278	2	My manager isn't someone I can be honest with	f	160	1
1279	2	Admitting difficulties gets used against me	f	160	1
1280	2	Other–I'll expain futher in the text box	t	160	1
1289	1	I regularly review what's going well	f	162	1
1290	1	My team has a ritual for taking stock	f	162	1
1291	1	My manager prompts us to review what works	f	162	1
1292	1	Other–I'll expain futher in the text box	t	162	1
1293	2	I rarely stop to review what's working	f	162	1
1294	2	My team has no routine for this	f	162	1
373	1	I can see how my work makes a difference	f	63	1
1295	2	My manager never prompts us to reflect	f	162	1
1296	2	Other–I'll expain futher in the text box	t	162	1
1305	1	My tasks are pitched at the right challenge level	f	164	1
1306	1	My manager calibrates my work to stretch me	f	164	1
961	1	Leadership announces changes well ahead of time	f	121	1
962	1	My manager briefs me as soon as plans shift	f	121	1
963	1	We have a channel where changes are posted early	f	121	1
964	1	Other–I'll expain futher in the text box	t	121	1
433	1	I get the right information at the right time	f	73	1
434	1	Updates are easy to understand and act on	f	73	1
965	2	Leadership decides changes without early warning	f	121	1
436	2	Updates are delayed or hard to interpret	f	73	1
437	2	Information comes through multiple channels inconsistently	f	73	1
438	2	I spend time chasing clarity instead of progressing work	f	73	1
966	2	My manager hears about changes the same time I do	f	121	1
967	2	We have no reliable channel for change updates	f	121	1
968	2	Other–I'll expain futher in the text box	t	121	1
977	1	Leaders clearly link our work to the bigger mission	f	123	1
978	1	My manager shows me the impact of what I do	f	123	1
979	1	I get to see how my work helps customers or colleagues	f	123	1
980	1	Other–I'll expain futher in the text box	t	123	1
981	2	No one connects my work to a bigger goal	f	123	1
982	2	My manager rarely explains why my work matters	f	123	1
983	2	I never see the outcome of what I produce	f	123	1
984	2	Other–I'll expain futher in the text box	t	123	1
993	1	I had protected focus time to get things done	f	125	1
994	1	My priorities were clear so I knew what to tackle	f	125	1
995	1	Quick decisions kept my work moving	f	125	1
996	1	Other–I'll expain futher in the text box	t	125	1
997	2	Back-to-back meetings ate my working time	f	125	1
998	2	Unclear priorities meant I started and stopped a lot	f	125	1
999	2	I was blocked waiting on decisions or people	f	125	1
1000	2	Other–I'll expain futher in the text box	t	125	1
1009	1	Our team goals are written down and visible	f	127	1
459	1	Technology supports quality and speed in my work	f	77	1
1010	1	My manager regularly restates our priorities	f	127	1
1011	1	We align on what matters in team meetings	f	127	1
1012	1	Other–I'll expain futher in the text box	t	127	1
1013	2	Our goals have never been clearly set out	f	127	1
1014	2	My manager rarely talks about our direction	f	127	1
1015	2	Everyone seems to be working to different aims	f	127	1
1016	2	Other–I'll expain futher in the text box	t	127	1
1025	1	My manager shapes my work around my career goals	f	129	1
1026	1	My role naturally builds toward where I want to go	f	129	1
1027	1	I've steered my tasks toward my ambitions	f	129	1
1028	1	Other–I'll expain futher in the text box	t	129	1
1029	2	My manager doesn't factor in my career goals	f	129	1
1030	2	My day-to-day pulls me away from my goals	f	129	1
1031	2	No one's helping me line up work with ambitions	f	129	1
1032	2	Other–I'll expain futher in the text box	t	129	1
1041	1	Shared tools make reaching other teams easy	f	131	1
1042	1	Cross-team collaboration is actively encouraged	f	131	1
1043	1	I know who to go to in other teams	f	131	1
1044	1	Other–I'll expain futher in the text box	t	131	1
1045	2	We lack tools to work across teams easily	f	131	1
1046	2	Silos make other teams hard to reach	f	131	1
1047	2	Rigid channels slow down cross-team work	f	131	1
1048	2	Other–I'll expain futher in the text box	t	131	1
1057	1	My suggestions visibly shape decisions	f	133	1
1058	1	My manager follows up on points I raise	f	133	1
1059	1	I can see my input reflected in outcomes	f	133	1
1060	1	Other–I'll expain futher in the text box	t	133	1
1061	2	My input is heard then quietly ignored	f	133	1
1062	2	My manager rarely acts on what I raise	f	133	1
1063	2	Decisions go ahead regardless of my input	f	133	1
1064	2	Other–I'll expain futher in the text box	t	133	1
1073	1	My manager and I discussed my next step recently	f	135	1
1074	1	Career conversations happen on a regular basis	f	135	1
1075	1	We've agreed a plan for my progression	f	135	1
1076	1	Other–I'll expain futher in the text box	t	135	1
1077	2	My manager and I never discuss my future	f	135	1
1078	2	Career talks only happen at annual reviews if at all	f	135	1
1079	2	There's no plan for my next step	f	135	1
1080	2	Other–I'll expain futher in the text box	t	135	1
1089	1	Our processes are streamlined and sensible	f	137	1
1090	1	The tools I use fit the way I work	f	137	1
1091	1	I have freedom to work in the way that suits me	f	137	1
1092	1	Other–I'll expain futher in the text box	t	137	1
1093	2	Clunky processes make simple things hard	f	137	1
1094	2	My tools don't fit how I actually work	f	137	1
1095	2	Rigid rules force me to work inefficiently	f	137	1
1096	2	Other–I'll expain futher in the text box	t	137	1
969	1	My manager assigns me genuinely challenging work	f	122	1
970	1	I take on tasks slightly beyond my current level	f	122	1
971	1	My role is regularly reshaped to keep me growing	f	122	1
972	1	Other–I'll expain futher in the text box	t	122	1
973	2	My tasks have stayed the same for too long	f	122	1
974	2	My manager hasn't given me anything new to tackle	f	122	1
507	1	Communication helps me stay connected and aligned	f	85	1
975	2	There's no mechanism to take on harder work	f	122	1
976	2	Other–I'll expain futher in the text box	t	122	1
510	2	Poor communication causes avoidable friction	f	85	1
985	1	My team has a norm of jumping in to help	f	124	1
986	1	My manager actively steps in when I'm under pressure	f	124	1
987	1	Colleagues offer cover before I have to ask	f	124	1
988	1	Other–I'll expain futher in the text box	t	124	1
989	2	My team leaves people to sink or swim	f	124	1
990	2	My manager is absent when pressure builds	f	124	1
991	2	Asking colleagues for help is frowned upon	f	124	1
992	2	Other–I'll expain futher in the text box	t	124	1
1001	1	I block regular time to reflect on my week	f	126	1
1002	1	Reflection is built into my 1:1s with my manager	f	126	1
1003	1	My team holds a regular review or retro	f	126	1
1004	1	Other–I'll expain futher in the text box	t	126	1
1005	2	My week was too packed to stop and reflect	f	126	1
1006	2	Reflection never comes up in my 1:1s	f	126	1
1007	2	My team has no routine for stepping back	f	126	1
1008	2	Other–I'll expain futher in the text box	t	126	1
1017	1	I ring-fence time for learning each week	f	128	1
1018	1	My manager and I track a development plan	f	128	1
1019	1	I actively seek out courses or reading	f	128	1
1020	1	Other–I'll expain futher in the text box	t	128	1
1021	2	Day-to-day work crowds out any learning time	f	128	1
1022	2	My manager and I have no development plan	f	128	1
1023	2	I haven't made time to develop myself	f	128	1
1024	2	Other–I'll expain futher in the text box	t	128	1
1033	1	Leaders actively invite honest opinions	f	130	1
1034	1	My team treats candid views as normal	f	130	1
1035	1	My manager makes it safe to disagree	f	130	1
1036	1	Other–I'll expain futher in the text box	t	130	1
1037	2	Honest opinions tend to get shut down here	f	130	1
1038	2	My team avoids saying what they really think	f	130	1
1039	2	Disagreeing with my manager feels risky	f	130	1
1040	2	Other–I'll expain futher in the text box	t	130	1
1049	1	My manager gives me specific and useful feedback	f	132	1
1050	1	We have regular feedback conversations	f	132	1
1051	1	Feedback comes with clear actions to try	f	132	1
1052	1	Other–I'll expain futher in the text box	t	132	1
1053	2	My manager rarely gives me real feedback	f	132	1
1054	2	We have no regular feedback rhythm	f	132	1
1055	2	Feedback I get is too vague to use	f	132	1
1056	2	Other–I'll expain futher in the text box	t	132	1
1065	1	I turned recent feedback into concrete change	f	134	1
1066	1	My manager coaches me on how to improve	f	134	1
1067	1	Feedback pointed me to a clear next skill	f	134	1
1068	1	Other–I'll expain futher in the text box	t	134	1
1069	2	Recent feedback was too vague to grow from	f	134	1
1070	2	My manager gives ratings but no coaching	f	134	1
1071	2	I haven't had useful feedback to act on	f	134	1
1072	2	Other–I'll expain futher in the text box	t	134	1
1081	1	We treat mistakes as learning rather than blame	f	136	1
1082	1	Leaders openly own their own mistakes	f	136	1
1083	1	My manager responds calmly when I slip up	f	136	1
1084	1	Other–I'll expain futher in the text box	t	136	1
1085	2	Mistakes here get punished rather than learned from	f	136	1
1086	2	No one senior ever admits getting it wrong	f	136	1
1087	2	I fear how my manager reacts to errors	f	136	1
1088	2	Other–I'll expain futher in the text box	t	136	1
1097	1	My workload is balanced enough to think clearly	f	138	1
1098	1	My manager and I keep my priorities realistic	f	138	1
1099	1	I protect some slack in my week to step back	f	138	1
1100	1	Other–I'll expain futher in the text box	t	138	1
1101	2	My workload is so full I can't think	f	138	1
1102	2	My priorities aren't managed down with my manager	f	138	1
1103	2	Every hour is booked with no room to reflect	f	138	1
1104	2	Other–I'll expain futher in the text box	t	138	1
1113	1	The training matched what my role actually needs	f	140	1
1114	1	I got to apply the training straight away	f	140	1
1115	1	My manager helped me put the learning into practice	f	140	1
1116	1	Other–I'll expain futher in the text box	t	140	1
1117	2	The training wasn't relevant to my work	f	140	1
1105	1	Recognition is a regular habit on my team	f	139	1
1106	1	My manager acknowledged my work recently	f	139	1
1107	1	A colleague thanked me for something I did	f	139	1
1108	1	Other–I'll expain futher in the text box	t	139	1
1109	2	Good work routinely goes unnoticed here	f	139	1
1110	2	My manager rarely acknowledges effort	f	139	1
1111	2	There's no culture of thanking each other	f	139	1
1112	2	Other–I'll expain futher in the text box	t	139	1
1121	1	Internal roles are openly advertised to everyone	f	141	1
1122	1	My manager encourages me to apply for moves	f	141	1
1123	1	I'm given time and backing to pursue openings	f	141	1
1124	1	Other–I'll expain futher in the text box	t	141	1
1125	2	Internal opportunities aren't shared openly	f	141	1
1126	2	My manager discourages me from moving on	f	141	1
1127	2	There's no support to pursue internal roles	f	141	1
1128	2	Other–I'll expain futher in the text box	t	141	1
1137	1	I have the right tools for my job	f	143	1
1138	1	I have the access and permissions I need	f	143	1
1139	1	My tools are reliable and rarely fail	f	143	1
1140	1	Other–I'll expain futher in the text box	t	143	1
1141	2	I'm missing tools I need to do my job	f	143	1
1142	2	Access or permission gaps block me	f	143	1
1143	2	My tools are slow or break too often	f	143	1
1144	2	Other–I'll expain futher in the text box	t	143	1
1153	1	Leaders act on what people raise	f	145	1
1154	1	We get told what happened to our feedback	f	145	1
1155	1	I've seen real change come from speaking up	f	145	1
1156	1	Other–I'll expain futher in the text box	t	145	1
1157	2	Leadership ignores what people raise	f	145	1
1158	2	Feedback disappears with no response	f	145	1
1159	2	Nothing ever changes no matter what I say	f	145	1
1160	2	Other–I'll expain futher in the text box	t	145	1
1169	1	I was handed a genuine stretch assignment	f	147	1
1170	1	My manager pushes me to try new things	f	147	1
1171	1	I volunteered for something unfamiliar	f	147	1
1172	1	Other–I'll expain futher in the text box	t	147	1
1173	2	I've had no stretch work recently	f	147	1
1174	2	My manager keeps me in safe familiar tasks	f	147	1
1175	2	There are no chances to stretch myself	f	147	1
1176	2	Other–I'll expain futher in the text box	t	147	1
1185	1	I'm trusted to decide how I do my work	f	149	1
613	1	I'm kept in the loop early enough to plan and adapt	f	103	1
614	1	The rationale behind decisions is explained clearly	f	103	1
1186	1	My manager focuses on outcomes rather than micromanaging	f	149	1
616	2	Decisions land late or without enough context	f	103	1
617	2	I'm left guessing why changes are being made	f	103	1
618	2	The lack of clarity creates confusion and rework	f	103	1
1187	1	I can organise my own time and approach	f	149	1
1188	1	Other–I'll expain futher in the text box	t	149	1
1189	2	I'm micromanaged on how I work	f	149	1
1190	2	My manager checks every small decision	f	149	1
1191	2	Rigid rules leave me little say	f	149	1
1192	2	Other–I'll expain futher in the text box	t	149	1
1201	1	Leaders explain the reasoning behind decisions	f	151	1
1202	1	My manager helps me understand the why	f	151	1
1203	1	The thinking behind decisions is shared openly	f	151	1
1204	1	Other–I'll expain futher in the text box	t	151	1
1205	2	Decisions arrive with no explanation	f	151	1
1206	2	My manager can't explain the reasoning either	f	151	1
1207	2	The real reasons are kept behind closed doors	f	151	1
1208	2	Other–I'll expain futher in the text box	t	151	1
1217	1	Our mission gives my work clear meaning	f	153	1
1218	1	My manager shows how my work fits the goal	f	153	1
1219	1	I can see my contribution making a difference	f	153	1
1220	1	Other–I'll expain futher in the text box	t	153	1
1221	2	I can't connect my work to anything bigger	f	153	1
1222	2	My manager never links my work to purpose	f	153	1
1223	2	My contribution feels invisible	f	153	1
1224	2	Other–I'll expain futher in the text box	t	153	1
1233	1	I have the tools and equipment I need	f	155	1
1234	1	I get the information I need to do my job	f	155	1
1235	1	Support is there when I need it	f	155	1
1236	1	Other–I'll expain futher in the text box	t	155	1
1237	2	I'm missing key tools or equipment	f	155	1
1238	2	I lack the information to do my job well	f	155	1
1239	2	Support is hard to come by when I need it	f	155	1
1240	2	Other–I'll expain futher in the text box	t	155	1
1249	1	My priorities for the week are clearly set	f	157	1
1250	1	My manager makes expectations explicit	f	157	1
1251	1	My objectives are written down and visible	f	157	1
1118	2	I never got to apply what was covered	f	140	1
1119	2	There was no follow-up to embed the learning	f	140	1
1120	2	Other–I'll expain futher in the text box	t	140	1
1129	1	My team takes time to understand each other	f	142	1
1130	1	My manager really listens to where I'm coming from	f	142	1
1131	1	We invest in knowing how each other works	f	142	1
1132	1	Other–I'll expain futher in the text box	t	142	1
1133	2	My team doesn't take time to understand me	f	142	1
1134	2	My manager doesn't really listen	f	142	1
1135	2	People make assumptions instead of asking	f	142	1
1136	2	Other–I'll expain futher in the text box	t	142	1
1145	1	My team takes time to mark our wins	f	144	1
1146	1	My manager called out a recent success	f	144	1
1147	1	I made a point of noting a win this week	f	144	1
1148	1	Other–I'll expain futher in the text box	t	144	1
1149	2	My team never pauses to celebrate	f	144	1
1150	2	My manager doesn't acknowledge successes	f	144	1
1151	2	We move straight to the next thing with no pause	f	144	1
1152	2	Other–I'll expain futher in the text box	t	144	1
1161	1	We have an easy-to-browse learning catalogue	f	146	1
1162	1	My manager points me to learning options	f	146	1
1163	1	Colleagues share useful learning resources	f	146	1
1164	1	Other–I'll expain futher in the text box	t	146	1
1165	2	There's no clear place to find learning	f	146	1
1166	2	My manager never signposts learning	f	146	1
1167	2	I have to hunt hard for any learning	f	146	1
1168	2	Other–I'll expain futher in the text box	t	146	1
1177	1	We get real time to connect and not just transact	f	148	1
1178	1	Team rituals help us bond beyond tasks	f	148	1
1179	1	My manager fosters genuine team relationships	f	148	1
1180	1	Other–I'll expain futher in the text box	t	148	1
1181	2	We only ever interact about tasks	f	148	1
1182	2	There's no time or space to connect	f	148	1
1183	2	Our setup keeps us distant from each other	f	148	1
1184	2	Other–I'll expain futher in the text box	t	148	1
1193	1	I regularly take stock of my strengths and gaps	f	150	1
1194	1	My manager and I review my development openly	f	150	1
1195	1	I use a tool or framework to assess myself	f	150	1
1196	1	Other–I'll expain futher in the text box	t	150	1
1197	2	I haven't paused to assess myself lately	f	150	1
1198	2	My manager and I never review my development	f	150	1
1199	2	I've no clear read on my strengths and gaps	f	150	1
1200	2	Other–I'll expain futher in the text box	t	150	1
686	1	I feel listened to and my input is understood	f	115	1
1209	1	This week's work taught me something new	f	152	1
1210	1	I was exposed to a new area or method	f	152	1
1211	1	A colleague or mentor showed me something	f	152	1
1212	1	Other–I'll expain futher in the text box	t	152	1
1213	2	My work this week was all familiar ground	f	152	1
1214	2	I had no exposure to anything new	f	152	1
1215	2	There was no one to learn from this week	f	152	1
1216	2	Other–I'll expain futher in the text box	t	152	1
1225	1	Asking for help is completely normal here	f	154	1
1226	1	My manager welcomes it when I ask for help	f	154	1
1227	1	My team responds well to help requests	f	154	1
1228	1	Other–I'll expain futher in the text box	t	154	1
1229	2	Asking for help is seen as weakness here	f	154	1
1230	2	My manager reacts poorly to help requests	f	154	1
1231	2	My team is unresponsive when I need help	f	154	1
1232	2	Other–I'll expain futher in the text box	t	154	1
1241	1	My 1:1s give space to raise recurring issues	f	156	1
1242	1	My manager is open to tackling persistent problems	f	156	1
1243	1	We track and work on sticking points together	f	156	1
1244	1	Other–I'll expain futher in the text box	t	156	1
1245	2	My 1:1s don't leave room for bigger issues	f	156	1
1246	2	My manager brushes off recurring problems	f	156	1
1247	2	Persistent issues never get followed up	f	156	1
1248	2	Other–I'll expain futher in the text box	t	156	1
1257	1	Building new skills is actively expected here	f	158	1
1258	1	My manager encourages me to learn new things	f	158	1
1259	1	I'm given time or budget to develop skills	f	158	1
528	2	I avoid being candid to protect myself	f	88	1
1260	1	Other–I'll expain futher in the text box	t	158	1
1261	2	Skill-building isn't encouraged here	f	158	1
1262	2	My manager shows no interest in my growth	f	158	1
1263	2	There's no time or budget to build skills	f	158	1
1264	2	Other–I'll expain futher in the text box	t	158	1
1273	1	Being open about struggles is normal here	f	160	1
1274	1	My manager makes it safe to share difficulties	f	160	1
1275	1	Admitting challenges isn't held against me	f	160	1
1252	1	Other–I'll expain futher in the text box	t	157	1
1253	2	My priorities this week are unclear	f	157	1
1254	2	My manager hasn't set clear expectations	f	157	1
1255	2	Expectations keep shifting without notice	f	157	1
1256	2	Other–I'll expain futher in the text box	t	157	1
1265	1	New challenges are regularly offered to me	f	159	1
1266	1	My manager delegates meaningful new work	f	159	1
721	2	Other–I'll expain futher in the text box	t	1	1
722	1	Other–I'll expain futher in the text box	t	1	1
723	2	Other–I'll expain futher in the text box	t	2	1
724	1	Other–I'll expain futher in the text box	t	2	1
725	2	Other–I'll expain futher in the text box	t	3	1
726	1	Other–I'll expain futher in the text box	t	3	1
727	2	Other–I'll expain futher in the text box	t	4	1
728	1	Other–I'll expain futher in the text box	t	4	1
729	2	Other–I'll expain futher in the text box	t	5	1
730	1	Other–I'll expain futher in the text box	t	5	1
731	2	Other–I'll expain futher in the text box	t	6	1
732	1	Other–I'll expain futher in the text box	t	6	1
733	2	Other–I'll expain futher in the text box	t	7	1
734	1	Other–I'll expain futher in the text box	t	7	1
735	2	Other–I'll expain futher in the text box	t	8	1
736	1	Other–I'll expain futher in the text box	t	8	1
737	2	Other–I'll expain futher in the text box	t	9	1
738	1	Other–I'll expain futher in the text box	t	9	1
739	2	Other–I'll expain futher in the text box	t	10	1
740	1	Other–I'll expain futher in the text box	t	10	1
741	2	Other–I'll expain futher in the text box	t	11	1
742	1	Other–I'll expain futher in the text box	t	11	1
743	2	Other–I'll expain futher in the text box	t	12	1
744	1	Other–I'll expain futher in the text box	t	12	1
745	2	Other–I'll expain futher in the text box	t	13	1
746	1	Other–I'll expain futher in the text box	t	13	1
747	2	Other–I'll expain futher in the text box	t	14	1
748	1	Other–I'll expain futher in the text box	t	14	1
749	2	Other–I'll expain futher in the text box	t	15	1
750	1	Other–I'll expain futher in the text box	t	15	1
751	2	Other–I'll expain futher in the text box	t	16	1
752	1	Other–I'll expain futher in the text box	t	16	1
753	2	Other–I'll expain futher in the text box	t	17	1
754	1	Other–I'll expain futher in the text box	t	17	1
755	2	Other–I'll expain futher in the text box	t	18	1
756	1	Other–I'll expain futher in the text box	t	18	1
757	2	Other–I'll expain futher in the text box	t	19	1
758	1	Other–I'll expain futher in the text box	t	19	1
759	2	Other–I'll expain futher in the text box	t	20	1
760	1	Other–I'll expain futher in the text box	t	20	1
761	2	Other–I'll expain futher in the text box	t	21	1
762	1	Other–I'll expain futher in the text box	t	21	1
763	2	Other–I'll expain futher in the text box	t	22	1
764	1	Other–I'll expain futher in the text box	t	22	1
765	2	Other–I'll expain futher in the text box	t	23	1
766	1	Other–I'll expain futher in the text box	t	23	1
767	2	Other–I'll expain futher in the text box	t	24	1
768	1	Other–I'll expain futher in the text box	t	24	1
769	2	Other–I'll expain futher in the text box	t	25	1
770	1	Other–I'll expain futher in the text box	t	25	1
771	2	Other–I'll expain futher in the text box	t	26	1
772	1	Other–I'll expain futher in the text box	t	26	1
773	2	Other–I'll expain futher in the text box	t	27	1
774	1	Other–I'll expain futher in the text box	t	27	1
775	2	Other–I'll expain futher in the text box	t	28	1
776	1	Other–I'll expain futher in the text box	t	28	1
777	2	Other–I'll expain futher in the text box	t	29	1
778	1	Other–I'll expain futher in the text box	t	29	1
779	2	Other–I'll expain futher in the text box	t	30	1
780	1	Other–I'll expain futher in the text box	t	30	1
781	2	Other–I'll expain futher in the text box	t	31	1
782	1	Other–I'll expain futher in the text box	t	31	1
783	2	Other–I'll expain futher in the text box	t	32	1
784	1	Other–I'll expain futher in the text box	t	32	1
785	2	Other–I'll expain futher in the text box	t	33	1
786	1	Other–I'll expain futher in the text box	t	33	1
787	2	Other–I'll expain futher in the text box	t	34	1
788	1	Other–I'll expain futher in the text box	t	34	1
789	2	Other–I'll expain futher in the text box	t	35	1
790	1	Other–I'll expain futher in the text box	t	35	1
791	2	Other–I'll expain futher in the text box	t	36	1
792	1	Other–I'll expain futher in the text box	t	36	1
793	2	Other–I'll expain futher in the text box	t	37	1
1267	1	I can put myself forward for projects	f	159	1
794	1	Other–I'll expain futher in the text box	t	37	1
795	2	Other–I'll expain futher in the text box	t	38	1
796	1	Other–I'll expain futher in the text box	t	38	1
797	2	Other–I'll expain futher in the text box	t	39	1
798	1	Other–I'll expain futher in the text box	t	39	1
799	2	Other–I'll expain futher in the text box	t	40	1
800	1	Other–I'll expain futher in the text box	t	40	1
801	2	Other–I'll expain futher in the text box	t	41	1
802	1	Other–I'll expain futher in the text box	t	41	1
803	2	Other–I'll expain futher in the text box	t	42	1
804	1	Other–I'll expain futher in the text box	t	42	1
805	2	Other–I'll expain futher in the text box	t	43	1
806	1	Other–I'll expain futher in the text box	t	43	1
807	2	Other–I'll expain futher in the text box	t	44	1
808	1	Other–I'll expain futher in the text box	t	44	1
809	2	Other–I'll expain futher in the text box	t	45	1
810	1	Other–I'll expain futher in the text box	t	45	1
811	2	Other–I'll expain futher in the text box	t	46	1
812	1	Other–I'll expain futher in the text box	t	46	1
813	2	Other–I'll expain futher in the text box	t	47	1
814	1	Other–I'll expain futher in the text box	t	47	1
815	2	Other–I'll expain futher in the text box	t	48	1
816	1	Other–I'll expain futher in the text box	t	48	1
817	2	Other–I'll expain futher in the text box	t	49	1
818	1	Other–I'll expain futher in the text box	t	49	1
819	2	Other–I'll expain futher in the text box	t	50	1
820	1	Other–I'll expain futher in the text box	t	50	1
821	2	Other–I'll expain futher in the text box	t	51	1
822	1	Other–I'll expain futher in the text box	t	51	1
823	2	Other–I'll expain futher in the text box	t	52	1
824	1	Other–I'll expain futher in the text box	t	52	1
825	2	Other–I'll expain futher in the text box	t	53	1
826	1	Other–I'll expain futher in the text box	t	53	1
827	2	Other–I'll expain futher in the text box	t	54	1
828	1	Other–I'll expain futher in the text box	t	54	1
829	2	Other–I'll expain futher in the text box	t	55	1
830	1	Other–I'll expain futher in the text box	t	55	1
831	2	Other–I'll expain futher in the text box	t	56	1
832	1	Other–I'll expain futher in the text box	t	56	1
833	2	Other–I'll expain futher in the text box	t	57	1
834	1	Other–I'll expain futher in the text box	t	57	1
835	2	Other–I'll expain futher in the text box	t	58	1
836	1	Other–I'll expain futher in the text box	t	58	1
837	2	Other–I'll expain futher in the text box	t	59	1
838	1	Other–I'll expain futher in the text box	t	59	1
839	2	Other–I'll expain futher in the text box	t	60	1
840	1	Other–I'll expain futher in the text box	t	60	1
841	2	Other–I'll expain futher in the text box	t	61	1
842	1	Other–I'll expain futher in the text box	t	61	1
843	2	Other–I'll expain futher in the text box	t	62	1
844	1	Other–I'll expain futher in the text box	t	62	1
845	2	Other–I'll expain futher in the text box	t	63	1
846	1	Other–I'll expain futher in the text box	t	63	1
847	2	Other–I'll expain futher in the text box	t	64	1
848	1	Other–I'll expain futher in the text box	t	64	1
849	2	Other–I'll expain futher in the text box	t	65	1
850	1	Other–I'll expain futher in the text box	t	65	1
851	2	Other–I'll expain futher in the text box	t	66	1
852	1	Other–I'll expain futher in the text box	t	66	1
853	2	Other–I'll expain futher in the text box	t	67	1
854	1	Other–I'll expain futher in the text box	t	67	1
855	2	Other–I'll expain futher in the text box	t	68	1
856	1	Other–I'll expain futher in the text box	t	68	1
857	2	Other–I'll expain futher in the text box	t	69	1
858	1	Other–I'll expain futher in the text box	t	69	1
859	2	Other–I'll expain futher in the text box	t	70	1
860	1	Other–I'll expain futher in the text box	t	70	1
861	2	Other–I'll expain futher in the text box	t	71	1
862	1	Other–I'll expain futher in the text box	t	71	1
863	2	Other–I'll expain futher in the text box	t	72	1
864	1	Other–I'll expain futher in the text box	t	72	1
865	2	Other–I'll expain futher in the text box	t	73	1
866	1	Other–I'll expain futher in the text box	t	73	1
867	2	Other–I'll expain futher in the text box	t	74	1
868	1	Other–I'll expain futher in the text box	t	74	1
869	2	Other–I'll expain futher in the text box	t	75	1
870	1	Other–I'll expain futher in the text box	t	75	1
871	2	Other–I'll expain futher in the text box	t	76	1
872	1	Other–I'll expain futher in the text box	t	76	1
873	2	Other–I'll expain futher in the text box	t	77	1
874	1	Other–I'll expain futher in the text box	t	77	1
875	2	Other–I'll expain futher in the text box	t	78	1
876	1	Other–I'll expain futher in the text box	t	78	1
877	2	Other–I'll expain futher in the text box	t	79	1
878	1	Other–I'll expain futher in the text box	t	79	1
879	2	Other–I'll expain futher in the text box	t	80	1
880	1	Other–I'll expain futher in the text box	t	80	1
881	2	Other–I'll expain futher in the text box	t	81	1
882	1	Other–I'll expain futher in the text box	t	81	1
883	2	Other–I'll expain futher in the text box	t	82	1
884	1	Other–I'll expain futher in the text box	t	82	1
885	2	Other–I'll expain futher in the text box	t	83	1
886	1	Other–I'll expain futher in the text box	t	83	1
887	2	Other–I'll expain futher in the text box	t	84	1
888	1	Other–I'll expain futher in the text box	t	84	1
889	2	Other–I'll expain futher in the text box	t	85	1
890	1	Other–I'll expain futher in the text box	t	85	1
891	2	Other–I'll expain futher in the text box	t	86	1
892	1	Other–I'll expain futher in the text box	t	86	1
893	2	Other–I'll expain futher in the text box	t	87	1
894	1	Other–I'll expain futher in the text box	t	87	1
895	2	Other–I'll expain futher in the text box	t	88	1
896	1	Other–I'll expain futher in the text box	t	88	1
897	2	Other–I'll expain futher in the text box	t	89	1
898	1	Other–I'll expain futher in the text box	t	89	1
899	2	Other–I'll expain futher in the text box	t	90	1
900	1	Other–I'll expain futher in the text box	t	90	1
901	2	Other–I'll expain futher in the text box	t	91	1
902	1	Other–I'll expain futher in the text box	t	91	1
903	2	Other–I'll expain futher in the text box	t	92	1
904	1	Other–I'll expain futher in the text box	t	92	1
905	2	Other–I'll expain futher in the text box	t	93	1
906	1	Other–I'll expain futher in the text box	t	93	1
907	2	Other–I'll expain futher in the text box	t	94	1
908	1	Other–I'll expain futher in the text box	t	94	1
909	2	Other–I'll expain futher in the text box	t	95	1
910	1	Other–I'll expain futher in the text box	t	95	1
911	2	Other–I'll expain futher in the text box	t	96	1
912	1	Other–I'll expain futher in the text box	t	96	1
913	2	Other–I'll expain futher in the text box	t	97	1
914	1	Other–I'll expain futher in the text box	t	97	1
915	2	Other–I'll expain futher in the text box	t	98	1
916	1	Other–I'll expain futher in the text box	t	98	1
917	2	Other–I'll expain futher in the text box	t	99	1
918	1	Other–I'll expain futher in the text box	t	99	1
919	2	Other–I'll expain futher in the text box	t	100	1
920	1	Other–I'll expain futher in the text box	t	100	1
921	2	Other–I'll expain futher in the text box	t	101	1
922	1	Other–I'll expain futher in the text box	t	101	1
923	2	Other–I'll expain futher in the text box	t	102	1
924	1	Other–I'll expain futher in the text box	t	102	1
925	2	Other–I'll expain futher in the text box	t	103	1
926	1	Other–I'll expain futher in the text box	t	103	1
927	2	Other–I'll expain futher in the text box	t	104	1
928	1	Other–I'll expain futher in the text box	t	104	1
929	2	Other–I'll expain futher in the text box	t	105	1
930	1	Other–I'll expain futher in the text box	t	105	1
931	2	Other–I'll expain futher in the text box	t	106	1
932	1	Other–I'll expain futher in the text box	t	106	1
933	2	Other–I'll expain futher in the text box	t	107	1
934	1	Other–I'll expain futher in the text box	t	107	1
935	2	Other–I'll expain futher in the text box	t	108	1
936	1	Other–I'll expain futher in the text box	t	108	1
937	2	Other–I'll expain futher in the text box	t	109	1
938	1	Other–I'll expain futher in the text box	t	109	1
939	2	Other–I'll expain futher in the text box	t	110	1
940	1	Other–I'll expain futher in the text box	t	110	1
941	2	Other–I'll expain futher in the text box	t	111	1
942	1	Other–I'll expain futher in the text box	t	111	1
943	2	Other–I'll expain futher in the text box	t	112	1
944	1	Other–I'll expain futher in the text box	t	112	1
945	2	Other–I'll expain futher in the text box	t	113	1
946	1	Other–I'll expain futher in the text box	t	113	1
947	2	Other–I'll expain futher in the text box	t	114	1
948	1	Other–I'll expain futher in the text box	t	114	1
949	2	Other–I'll expain futher in the text box	t	115	1
950	1	Other–I'll expain futher in the text box	t	115	1
951	2	Other–I'll expain futher in the text box	t	116	1
952	1	Other–I'll expain futher in the text box	t	116	1
953	2	Other–I'll expain futher in the text box	t	117	1
954	1	Other–I'll expain futher in the text box	t	117	1
955	2	Other–I'll expain futher in the text box	t	118	1
956	1	Other–I'll expain futher in the text box	t	118	1
957	2	Other–I'll expain futher in the text box	t	119	1
958	1	Other–I'll expain futher in the text box	t	119	1
959	2	Other–I'll expain futher in the text box	t	120	1
960	1	Other–I'll expain futher in the text box	t	120	1
369	1	Learning feels relevant to my role and goals	f	62	1
370	2	There's limited space to learn something new	f	62	1
372	2	Learning gets squeezed out by day-to-day demands	f	62	1
374	1	My contributions feel valued and purposeful	f	63	1
375	1	I'm motivated because the work feels worthwhile	f	63	1
376	2	It's hard to see the impact of what I do	f	63	1
377	2	My work feels disconnected from meaningful outcomes	f	63	1
378	2	This reduces my motivation and sense of purpose	f	63	1
379	1	I feel safe to be honest without fear of repercussions	f	64	1
380	1	People respond with respect when I speak up	f	64	1
381	1	Openness is met with support rather than judgement	f	64	1
382	2	I worry there will be negative consequences if I speak up	f	64	1
383	2	Honesty feels risky in the current environment	f	64	1
385	1	I have the clarity and support I need to perform well	f	65	1
386	1	Dependencies are handled efficiently when they arise	f	65	1
387	1	I can make steady progress without major blockers	f	65	1
388	2	Blockers regularly prevent me from progressing work	f	65	1
390	2	It's difficult to deliver despite strong effort	f	65	1
391	1	I've had a useful conversation that helped me think clearly	f	66	1
392	1	We explored options and next steps constructively	f	66	1
393	1	I left the conversation with more confidence and direction	f	66	1
394	2	I haven't had space to talk through ongoing challenges	f	66	1
395	2	Conversations don't lead to clarity or action	f	66	1
403	1	I've picked up useful new knowledge through my work	f	68	1
404	1	I have chances to practice and build skills	f	68	1
405	1	Learning feels relevant to my role and goals	f	68	1
406	2	There's limited space to learn something new	f	68	1
408	2	Learning gets squeezed out by day-to-day demands	f	68	1
409	1	I'm given opportunities to take on meaningful challenges	f	69	1
410	1	I can stretch beyond my usual scope in a supported way	f	69	1
411	1	New opportunities help me build confidence and momentum	f	69	1
412	2	Opportunities to stretch are limited or inaccessible	f	69	1
413	2	I'm not trusted with new or interesting work	f	69	1
414	2	I feel my growth is being held back	f	69	1
415	1	I can raise concerns and be taken seriously	f	70	1
416	1	Mistakes are treated as learning opportunities	f	70	1
417	1	I'm comfortable being candid in conversations	f	70	1
418	2	Concerns are dismissed or not addressed	f	70	1
419	2	There's blame or defensiveness when issues come up	f	70	1
420	2	I avoid being candid to protect myself	f	70	1
421	1	Processes are clear and help work move forward	f	71	1
422	1	I can navigate approvals and steps without delays	f	71	1
423	1	Ways of working reduce friction and confusion	f	71	1
424	2	Processes create unnecessary steps or bottlenecks	f	71	1
425	2	It's hard to get decisions or approvals quickly	f	71	1
426	2	Process friction makes progress feel slow	f	71	1
427	1	I regularly pause to reflect and learn from my work	f	72	1
430	2	I rarely get time or headspace to reflect	f	72	1
431	2	Reflection isn't encouraged or built into how we work	f	72	1
435	1	Communication keeps work moving smoothly	f	73	1
440	1	I get feedback or guidance that helps me improve	f	74	1
441	1	I feel my skills are moving forward	f	74	1
442	2	Challenges aren't translating into learning	f	74	1
443	2	I'm not getting the feedback or support I need to grow	f	74	1
444	2	I feel stuck rather than developing	f	74	1
445	1	I can see clear options for progression here	f	75	1
446	1	I'm encouraged to explore pathways and next steps	f	75	1
447	1	There's support to move toward roles that fit my strengths	f	75	1
448	2	Progression feels unclear or out of reach	f	75	1
449	2	I don't feel supported to explore new paths	f	75	1
247	1	I received specific, actionable suggestions	f	42	1
181	1	Respect is maintained at all levels	f	31	1
182	1	Interactions are respectful and constructive	f	31	1
183	1	Positive interactions have me enjoying my work	f	31	1
184	2	I feel disrespected or undervalued in some exchanges	f	31	1
185	2	Management is dismissive in some interactions	f	31	1
186	2	My colleagues are uncooperative or obstructive	f	31	1
187	1	I have learned valuable lessons from recent tasks	f	32	1
188	1	Current tasks match my growth goals and aspirations	f	32	1
189	1	I overcame complex problems that improved my skills	f	32	1
190	2	Current tasks are routine and offer little new learning	f	32	1
191	2	I feel too busy to focus on growth	f	32	1
192	2	Lack of opportunity to reflect on challenges and get insights	f	32	1
193	1	I know how my work connects to the organisations goals	f	33	1
194	1	I got great feedback on something I did	f	33	1
195	1	Recently I got time to progress something important and valuable	f	33	1
196	2	I am overburdened with operational or reactive work	f	33	1
197	2	All my time is spent in meetings	f	33	1
198	2	None of my input is acknowledged	f	33	1
199	1	My team provide timely and helpful support	f	34	1
200	1	I feel a strong sense of camaraderie from my team	f	34	1
201	1	My team actively offers assistance before I have to ask	f	34	1
202	2	My team has been unavailable or indifferent to my needs	f	34	1
203	2	The support offered was ineffective or irrelevant	f	34	1
204	2	Our team environment feels competitive rather than supportive	f	34	1
205	1	Working collaboratively with colleagues has been very productive	f	35	1
206	1	I got some time to focus and got some important things done	f	35	1
207	1	We had some clear agreed decisions	f	35	1
208	2	Too much time in unproductive meetings	f	35	1
209	2	Too many competing priorities	f	35	1
210	2	The people I needed aren't available or are difficult to get time from	f	35	1
211	1	I make time to reflect as I value this	f	36	1
212	1	We reflect as a team on a regular schedule	f	36	1
213	1	I reflect as part of a regular conversation with my manager	f	36	1
215	2	Reflection is not seen as a priority in our daily routine	f	36	1
216	2	I need help learning how to effectively reflect on my daily work	f	36	1
217	1	Communications are clear and direct	f	37	1
218	1	I receive comprehensive information when needed	f	37	1
219	1	There is a focus on ensuring mutual understanding	f	37	1
220	2	Messages are ambiguous and lead to confusion	f	37	1
221	2	Communication is clear but the number of messages is overwhelming	f	37	1
222	2	Information is fragmented and inconsistent	f	37	1
223	1	I have actively engaged in activities that promote self-improvement	f	38	1
224	1	My current development opportunities align with my career goals	f	38	1
225	1	I feel motivated by the progression in my skills and knowledge	f	38	1
226	2	Development feels like a low priority compared to other tasks	f	38	1
227	2	There are no opportunities for personal development here	f	38	1
228	2	I do not see clear value in the available development choices	f	38	1
229	1	Management supports me in align the work I'm focused on with my career goals	f	39	1
230	1	The opportunities here resonate with my career ambitions	f	39	1
231	1	My career milestones are in sync with personal goals	f	39	1
232	2	Career progression seems limited or off course	f	39	1
233	2	No one is helping me align my work with my career goals	f	39	1
234	2	Career path feels disconnected from personal aspirations	f	39	1
235	1	The culture encourages open and honest dialogue	f	40	1
236	1	Our leaders model talking about and learning from failures	f	40	1
237	1	Received positive reinforcement when providing feedback	f	40	1
238	2	I hesitate sharing my true thoughts due to fear of negative repercussions	f	40	1
239	2	There was no channel to give feedback	f	40	1
240	2	Past experiences have made me wary of sharing	f	40	1
241	1	Tools and systems facilitate easy connection	f	41	1
242	1	There is a strong sense of community and teamwork	f	41	1
243	1	Cross team collaboration is encouraged	f	41	1
244	2	Collaboration is hindered by silos or lack of tools	f	41	1
245	2	Difficult to find opportunities for meaningful connection	f	41	1
246	2	There are strict channels to be followed to engage with other teams	f	41	1
248	1	I got feedback that helped to clarify expectations	f	42	1
249	1	Feedback included positive reinforcement	f	42	1
250	2	Feedback was absent or not relevant	f	42	1
251	2	Criticism overshadowed constructive feedback	f	42	1
252	2	Feedback was provided too late to make a difference	f	42	1
253	1	I feel actively listened to and acknowledged	f	43	1
254	1	My input is valued and prompts further discussion	f	43	1
255	1	There is a mutual understanding in all conversations	f	43	1
256	2	Others seemed distracted or disinterested in my input	f	43	1
257	2	My points are misinterpreted or overlooked	f	43	1
258	2	There is a lack of effort to clarify misunderstandings	f	43	1
259	1	I got feedback that was helpful and timely	f	44	1
260	1	I got very constructive feedback that led me to try a new approach	f	44	1
261	1	I received feedback that provided a useful new perspective	f	44	1
262	2	Feedback has been patchy at best	f	44	1
263	2	Feedback focuses on negatives without guidance	f	44	1
264	2	I receive feedback too late to be actionable	f	44	1
265	1	Yes - I'm following a clear growth path	f	45	1
266	1	We allocate time to work on growing our skills	f	45	1
267	1	We're allowed to support initiatives outside our team to support our growth	f	45	1
268	2	We have that conversation once or twice a year but it's hollow	f	45	1
269	2	I feel I have stagnated in my current role	f	45	1
270	2	I can't talk to my manager about opportunities beyond our team	f	45	1
271	1	We openly discuss our failures or mistakes as learning opportunities	f	46	1
272	1	There is a non-judgmental approach to addressing failures	f	46	1
273	1	Our leadership encourages and models transparent dialogue	f	46	1
274	2	Fear of blame or repercussions hinders open discussions	f	46	1
275	2	Lack of a safe space to discuss and learn from failures	f	46	1
276	2	Failures are often ignored or stigmatized - not discussed	f	46	1
277	1	Our methods align with my personal strengths	f	47	1
278	1	We have autonomy to choose our methods	f	47	1
279	1	Our methods allow for creativity and innovation	f	47	1
280	2	Methods are imposed rather than chosen	f	47	1
281	2	Our methods are inefficient which causes me frustration	f	47	1
282	2	The methods dont allow for personal work style	f	47	1
283	1	I got time to focus and got something valuable completed	f	48	1
284	1	We have good priortisation processes	f	48	1
285	1	I'm balancing several tasks but not too many	f	48	1
286	2	Too many competing priorities	f	48	1
287	2	Things take way longer than people allow for	f	48	1
288	2	We're constantly overcommitted	f	48	1
289	1	I've received direct recognition for efforts from peers or management	f	49	1
290	1	Acknowledgement for my work has felt meaningful and personalized	f	49	1
291	1	My contributions led to visible outcomes	f	49	1
292	2	My efforts went unnoticed or unappreciated	f	49	1
293	2	Acknowledgement lacked sincerity or specificity	f	49	1
294	2	My positive contributions were overshadowed by others	f	49	1
296	1	We're given good opportunities to participate in valuable training	f	50	1
297	1	It was a while ago - but was very valuable	f	50	1
298	2	Haven't had any training opportunities recently	f	50	1
299	2	The training we're offered is not valuable	f	50	1
300	2	I'm busy with a great training oportunity right now	f	50	1
301	1	I've discussed several opportunities with my manager	f	51	1
302	1	There is an open portal where we can apply and I've used it	f	51	1
303	1	I've already moved internally through an internal opportunity	f	51	1
304	2	This would be seen as betraying my manager	f	51	1
305	2	There's no channel or no proper information about internal opportunities	f	51	1
306	2	I've tried more than once but been rejected in favour of external hires	f	51	1
307	1	I feel actively listened to and acknowledged	f	52	1
308	1	Management is supportive in ensuring we're understood	f	52	1
309	1	There is mutual understanding in most conversations	f	52	1
310	2	Others seem distracted or disinterested in my input	f	52	1
311	2	My points are misinterpreted or overlooked	f	52	1
312	2	There is a lack of effort to clarify misunderstandings	f	52	1
313	1	Tools are up-to-date and user-friendly	f	53	1
314	1	Our tools improve my task completion speed	f	53	1
315	1	I'm highly satisfied with our seamless technology experience	f	53	1
316	2	Tools are difficult to use or malfunctioning	f	53	1
317	2	Lack of tool training hinders our efficiency	f	53	1
318	2	The tools don't integrate well with other systems	f	53	1
295	1	Workload is too high to allow time for training	f	50	1
363	1	Updates help me stay aligned with what matters most	f	61	1
367	1	I've picked up useful new knowledge through my work	f	62	1
368	1	I have chances to practice and build skills	f	62	1
214	2	Overwhelmed, no time for reflection	f	36	1
450	2	I'm unsure what opportunities exist for me here	f	75	1
451	1	I feel accepted and able to be myself at work	f	76	1
452	1	People show empathy when challenges arise	f	76	1
453	1	There's trust to share difficult things openly	f	76	1
454	2	I feel guarded about what I share	f	76	1
455	2	I don't experience much empathy or understanding	f	76	1
456	2	This affects my confidence and willingness to engage	f	76	1
457	1	The tools I use are reliable and fit for purpose	f	77	1
458	1	Systems make it easy to get work done efficiently	f	77	1
460	2	Tools slow me down or don't work as needed	f	77	1
461	2	Systems are clunky or don't integrate well	f	77	1
462	2	I waste time working around tool limitations	f	77	1
463	1	I regularly pause to reflect and learn from my work	f	78	1
466	2	I rarely get time or headspace to reflect	f	78	1
467	2	Reflection isn't encouraged or built into how we work	f	78	1
470	1	I feel listened to and my input is understood	f	79	1
471	1	Communication helps me stay connected and aligned	f	79	1
473	2	I don't feel heard when I share my perspective	f	79	1
474	2	Poor communication causes avoidable friction	f	79	1
476	1	I get feedback or guidance that helps me improve	f	80	1
477	1	I feel my skills are moving forward	f	80	1
478	2	Challenges aren't translating into learning	f	80	1
479	2	I'm not getting the feedback or support I need to grow	f	80	1
480	2	I feel stuck rather than developing	f	80	1
481	1	I'm trusted with work that uses my strengths	f	81	1
482	1	My ideas have space to be explored	f	81	1
483	1	I feel there are options to grow and progress	f	81	1
484	2	My strengths feel underused	f	81	1
485	2	Ideas don't get traction or follow-through	f	81	1
486	2	Opportunities to progress feel limited	f	81	1
487	1	I feel safe to be honest without fear of repercussions	f	82	1
488	1	People respond with respect when I speak up	f	82	1
489	1	Openness is met with support rather than judgement	f	82	1
490	2	I worry there will be negative consequences if I speak up	f	82	1
491	2	Honesty feels risky in the current environment	f	82	1
493	1	My workload feels manageable and well-prioritised	f	83	1
494	1	I have enough focus time to make meaningful progress	f	83	1
495	1	I can plan my work without constant disruption	f	83	1
496	2	Competing priorities make it hard to focus	f	83	1
497	2	My workload feels too heavy or unpredictable	f	83	1
498	2	Interruptions and churn reduce my productivity	f	83	1
499	1	I regularly pause to reflect and learn from my work	f	84	1
502	2	I rarely get time or headspace to reflect	f	84	1
503	2	Reflection isn't encouraged or built into how we work	f	84	1
506	1	I feel listened to and my input is understood	f	85	1
509	2	I don't feel heard when I share my perspective	f	85	1
511	1	I've picked up useful new knowledge through my work	f	86	1
512	1	I have chances to practice and build skills	f	86	1
513	1	Learning feels relevant to my role and goals	f	86	1
514	2	There's limited space to learn something new	f	86	1
516	2	Learning gets squeezed out by day-to-day demands	f	86	1
517	1	I'm trusted with work that uses my strengths	f	87	1
518	1	My ideas have space to be explored	f	87	1
519	1	I feel there are options to grow and progress	f	87	1
520	2	My strengths feel underused	f	87	1
521	2	Ideas don't get traction or follow-through	f	87	1
522	2	Opportunities to progress feel limited	f	87	1
523	1	I can raise concerns and be taken seriously	f	88	1
524	1	Mistakes are treated as learning opportunities	f	88	1
525	1	I'm comfortable being candid in conversations	f	88	1
526	2	Concerns are dismissed or not addressed	f	88	1
527	2	There's blame or defensiveness when issues come up	f	88	1
529	1	My workload feels manageable and well-prioritised	f	89	1
530	1	I have enough focus time to make meaningful progress	f	89	1
531	1	I can plan my work without constant disruption	f	89	1
532	2	Competing priorities make it hard to focus	f	89	1
533	2	My workload feels too heavy or unpredictable	f	89	1
534	2	Interruptions and churn reduce my productivity	f	89	1
535	1	I regularly pause to reflect and learn from my work	f	90	1
538	2	I rarely get time or headspace to reflect	f	90	1
539	2	Reflection isn't encouraged or built into how we work	f	90	1
542	1	I feel listened to and my input is understood	f	91	1
543	1	Communication helps me stay connected and aligned	f	91	1
545	2	I don't feel heard when I share my perspective	f	91	1
546	2	Poor communication causes avoidable friction	f	91	1
547	1	I've picked up useful new knowledge through my work	f	92	1
548	1	I have chances to practice and build skills	f	92	1
549	1	Learning feels relevant to my role and goals	f	92	1
550	2	There's limited space to learn something new	f	92	1
552	2	Learning gets squeezed out by day-to-day demands	f	92	1
553	1	I'm trusted with work that uses my strengths	f	93	1
554	1	My ideas have space to be explored	f	93	1
555	1	I feel there are options to grow and progress	f	93	1
556	2	My strengths feel underused	f	93	1
557	2	Ideas don't get traction or follow-through	f	93	1
558	2	Opportunities to progress feel limited	f	93	1
559	1	I can raise concerns and be taken seriously	f	94	1
560	1	Mistakes are treated as learning opportunities	f	94	1
561	1	I'm comfortable being candid in conversations	f	94	1
562	2	Concerns are dismissed or not addressed	f	94	1
563	2	There's blame or defensiveness when issues come up	f	94	1
564	2	I avoid being candid to protect myself	f	94	1
565	1	The tools I use are reliable and fit for purpose	f	95	1
566	1	Systems make it easy to get work done efficiently	f	95	1
567	1	Technology supports quality and speed in my work	f	95	1
568	2	Tools slow me down or don't work as needed	f	95	1
569	2	Systems are clunky or don't integrate well	f	95	1
570	2	I waste time working around tool limitations	f	95	1
571	1	I regularly pause to reflect and learn from my work	f	96	1
574	2	I rarely get time or headspace to reflect	f	96	1
575	2	Reflection isn't encouraged or built into how we work	f	96	1
578	1	I feel listened to and my input is understood	f	97	1
579	1	Communication helps me stay connected and aligned	f	97	1
581	2	I don't feel heard when I share my perspective	f	97	1
582	2	Poor communication causes avoidable friction	f	97	1
583	1	I've picked up useful new knowledge through my work	f	98	1
584	1	I have chances to practice and build skills	f	98	1
585	1	Learning feels relevant to my role and goals	f	98	1
586	2	There's limited space to learn something new	f	98	1
588	2	Learning gets squeezed out by day-to-day demands	f	98	1
589	1	I'm trusted with work that uses my strengths	f	99	1
590	1	My ideas have space to be explored	f	99	1
591	1	I feel there are options to grow and progress	f	99	1
592	2	My strengths feel underused	f	99	1
593	2	Ideas don't get traction or follow-through	f	99	1
594	2	Opportunities to progress feel limited	f	99	1
595	1	I feel accepted and able to be myself at work	f	100	1
596	1	People show empathy when challenges arise	f	100	1
597	1	There's trust to share difficult things openly	f	100	1
598	2	I feel guarded about what I share	f	100	1
599	2	I don't experience much empathy or understanding	f	100	1
600	2	This affects my confidence and willingness to engage	f	100	1
601	1	I have the clarity and support I need to perform well	f	101	1
602	1	Dependencies are handled efficiently when they arise	f	101	1
603	1	I can make steady progress without major blockers	f	101	1
1268	1	Other–I'll expain futher in the text box	t	159	1
604	2	Blockers regularly prevent me from progressing work	f	101	1
606	2	It's difficult to deliver despite strong effort	f	101	1
607	1	I regularly pause to reflect and learn from my work	f	102	1
610	2	I rarely get time or headspace to reflect	f	102	1
611	2	Reflection isn't encouraged or built into how we work	f	102	1
615	1	Updates help me stay aligned with what matters most	f	103	1
619	1	I've picked up useful new knowledge through my work	f	104	1
620	1	I have chances to practice and build skills	f	104	1
621	1	Learning feels relevant to my role and goals	f	104	1
622	2	There's limited space to learn something new	f	104	1
624	2	Learning gets squeezed out by day-to-day demands	f	104	1
625	1	I'm trusted with work that uses my strengths	f	105	1
626	1	My ideas have space to be explored	f	105	1
627	1	I feel there are options to grow and progress	f	105	1
628	2	My strengths feel underused	f	105	1
629	2	Ideas don't get traction or follow-through	f	105	1
630	2	Opportunities to progress feel limited	f	105	1
631	1	I can ask for help and get it quickly	f	106	1
632	1	People check in and offer support when needed	f	106	1
633	1	I feel backed by my team when things are hard	f	106	1
634	2	I don't feel comfortable asking for help	f	106	1
635	2	Support is inconsistent or hard to access	f	106	1
636	2	I'm left to manage challenges on my own	f	106	1
637	1	My workload feels manageable and well-prioritised	f	107	1
638	1	I have enough focus time to make meaningful progress	f	107	1
639	1	I can plan my work without constant disruption	f	107	1
640	2	Competing priorities make it hard to focus	f	107	1
641	2	My workload feels too heavy or unpredictable	f	107	1
642	2	Interruptions and churn reduce my productivity	f	107	1
643	1	I've had a useful conversation that helped me think clearly	f	108	1
644	1	We explored options and next steps constructively	f	108	1
645	1	I left the conversation with more confidence and direction	f	108	1
646	2	I haven't had space to talk through ongoing challenges	f	108	1
647	2	Conversations don't lead to clarity or action	f	108	1
648	2	Without reflection time issues tend to repeat	f	108	1
650	1	I feel listened to and my input is understood	f	109	1
651	1	Communication helps me stay connected and aligned	f	109	1
653	2	I don't feel heard when I share my perspective	f	109	1
654	2	Poor communication causes avoidable friction	f	109	1
655	1	I've picked up useful new knowledge through my work	f	110	1
656	1	I have chances to practice and build skills	f	110	1
657	1	Learning feels relevant to my role and goals	f	110	1
658	2	There's limited space to learn something new	f	110	1
660	2	Learning gets squeezed out by day-to-day demands	f	110	1
661	1	I can see how my work makes a difference	f	111	1
662	1	My contributions feel valued and purposeful	f	111	1
663	1	I'm motivated because the work feels worthwhile	f	111	1
664	2	It's hard to see the impact of what I do	f	111	1
665	2	My work feels disconnected from meaningful outcomes	f	111	1
666	2	This reduces my motivation and sense of purpose	f	111	1
667	1	I feel accepted and able to be myself at work	f	112	1
668	1	People show empathy when challenges arise	f	112	1
669	1	There's trust to share difficult things openly	f	112	1
670	2	I feel guarded about what I share	f	112	1
671	2	I don't experience much empathy or understanding	f	112	1
672	2	This affects my confidence and willingness to engage	f	112	1
673	1	I have the clarity and support I need to perform well	f	113	1
674	1	Dependencies are handled efficiently when they arise	f	113	1
675	1	I can make steady progress without major blockers	f	113	1
676	2	Blockers regularly prevent me from progressing work	f	113	1
678	2	It's difficult to deliver despite strong effort	f	113	1
679	1	I regularly pause to reflect and learn from my work	f	114	1
682	2	I rarely get time or headspace to reflect	f	114	1
683	2	Reflection isn't encouraged or built into how we work	f	114	1
687	1	Communication helps me stay connected and aligned	f	115	1
689	2	I don't feel heard when I share my perspective	f	115	1
690	2	Poor communication causes avoidable friction	f	115	1
691	1	I'm encouraged to develop and improve	f	116	1
692	1	I have access to resources or coaching that helps	f	116	1
693	1	I can see progress in my capability over time	f	116	1
694	2	Development isn't prioritised or supported	f	116	1
695	2	Resources are hard to access or not relevant	f	116	1
696	2	I don't feel I'm growing at the pace I'd like	f	116	1
697	1	I can see clear options for progression here	f	117	1
698	1	I'm encouraged to explore pathways and next steps	f	117	1
699	1	There's support to move toward roles that fit my strengths	f	117	1
700	2	Progression feels unclear or out of reach	f	117	1
701	2	I don't feel supported to explore new paths	f	117	1
702	2	I'm unsure what opportunities exist for me here	f	117	1
709	1	I have the clarity and support I need to perform well	f	119	1
710	1	Dependencies are handled efficiently when they arise	f	119	1
711	1	I can make steady progress without major blockers	f	119	1
712	2	Blockers regularly prevent me from progressing work	f	119	1
714	2	It's difficult to deliver despite strong effort	f	119	1
715	1	I regularly pause to reflect and learn from my work	f	120	1
718	2	I rarely get time or headspace to reflect	f	120	1
719	2	Reflection isn't encouraged or built into how we work	f	120	1
684	2	Without reflection, I miss chances to learn and improve	f	114	1
685	1	Conversations feel clear, respectful, and two-way	f	115	1
688	2	Messages are unclear, inconsistent, or easy to misread	f	115	1
713	2	I'm missing key information, access, or support	f	119	1
716	1	Reflection helps me reset, prioritise, and improve	f	120	1
717	1	Taking stock gives me clarity and direction	f	120	1
720	2	Without reflection, I miss chances to learn and improve	f	120	1
1269	2	I'm rarely offered anything new	f	159	1
1270	2	My manager keeps the interesting work	f	159	1
1271	2	There's no route to take on new challenges	f	159	1
1272	2	Other–I'll expain futher in the text box	t	159	1
1281	1	Our processes are lean and help me deliver	f	161	1
1282	1	Governance is proportionate rather than excessive	f	161	1
1283	1	We review and improve clunky processes	f	161	1
1284	1	Other–I'll expain futher in the text box	t	161	1
1285	2	Heavy processes slow everything down	f	161	1
1286	2	Excessive sign-offs get in the way	f	161	1
1287	2	Broken processes never get fixed	f	161	1
1288	2	Other–I'll expain futher in the text box	t	161	1
1297	1	Information reaches me in good time	f	163	1
1298	1	My manager passes on what I need promptly	f	163	1
1299	1	I have access to the information I need	f	163	1
1300	1	Other–I'll expain futher in the text box	t	163	1
1301	2	Information reaches me too late to use	f	163	1
1302	2	My manager is a bottleneck for information	f	163	1
1303	2	I lack access to information I need	f	163	1
1304	2	Other–I'll expain futher in the text box	t	163	1
1307	1	I have a healthy mix of familiar and new work	f	164	1
1308	1	Other–I'll expain futher in the text box	t	164	1
1309	2	My tasks are too routine to stretch me	f	164	1
1310	2	My manager doesn't calibrate my workload	f	164	1
1311	2	I'm either bored or overwhelmed and never balanced	f	164	1
1312	2	Other–I'll expain futher in the text box	t	164	1
1313	1	There are clear career paths I can see	f	165	1
1314	1	My manager invests in my long-term future	f	165	1
1315	1	I see others growing so I know I can too	f	165	1
1316	1	Other–I'll expain futher in the text box	t	165	1
1317	2	There's no visible path forward for me	f	165	1
1318	2	My manager shows no interest in my future	f	165	1
1319	2	People like me don't seem to progress here	f	165	1
1320	2	Other–I'll expain futher in the text box	t	165	1
1321	1	Our culture genuinely welcomes individuality	f	166	1
1322	1	My manager values what makes me different	f	166	1
1323	1	I feel a real sense of belonging here	f	166	1
1324	1	Other–I'll expain futher in the text box	t	166	1
1325	2	I feel I must conform to fit in	f	166	1
1326	2	My manager doesn't value my differences	f	166	1
1327	2	I don't feel I belong here	f	166	1
1328	2	Other–I'll expain futher in the text box	t	166	1
1329	1	My tools are well-suited to my tasks	f	167	1
1330	1	My tools work together without friction	f	167	1
1331	1	My tools are fast and reliable	f	167	1
1332	1	Other–I'll expain futher in the text box	t	167	1
1333	2	My tools aren't suited to my work	f	167	1
1334	2	My tools don't talk to each other	f	167	1
1335	2	My tools are slow or unreliable	f	167	1
1336	2	Other–I'll expain futher in the text box	t	167	1
1337	1	I make sure I implement improvement actions	f	168	1
1338	1	I agree improvements with my manager	f	168	1
1339	1	We prioritise and action improvements as a team	f	168	1
1340	1	Other–I'll expain futher in the text box	t	168	1
1341	2	I notice improvements but never action them	f	168	1
1342	2	My manager and I don't agree any changes	f	168	1
1343	2	My team never prioritises improvements	f	168	1
1344	2	Other–I'll expain futher in the text box	t	168	1
1345	1	The feedback I get is specific and clear	f	169	1
1346	1	My manager gives feedback I can act on	f	169	1
1347	1	Feedback comes with concrete examples	f	169	1
1348	1	Other–I'll expain futher in the text box	t	169	1
1349	2	Feedback I get is too vague to use	f	169	1
1350	2	My manager's feedback is unclear	f	169	1
1351	2	Feedback lacks examples to act on	f	169	1
1352	2	Other–I'll expain futher in the text box	t	169	1
1353	1	I get practical guidance that works	f	170	1
1354	1	My manager mentors me effectively	f	170	1
1355	1	I can reach experts when I need guidance	f	170	1
1356	1	Other–I'll expain futher in the text box	t	170	1
1357	2	The guidance I get doesn't really help	f	170	1
1358	2	My manager isn't able to mentor me well	f	170	1
1359	2	I've no one to turn to for good guidance	f	170	1
1360	2	Other–I'll expain futher in the text box	t	170	1
1361	1	My role lets me use my key strengths	f	171	1
1362	1	My manager assigns work to my strengths	f	171	1
1363	1	My strengths are known and drawn on	f	171	1
1364	1	Other–I'll expain futher in the text box	t	171	1
1365	2	My role rarely uses my real strengths	f	171	1
1366	2	My manager ignores what I'm good at	f	171	1
1367	2	No one knows or uses my strengths	f	171	1
1368	2	Other–I'll expain futher in the text box	t	171	1
1369	1	Flagging problems is welcomed here	f	172	1
1370	1	My manager acts on issues I raise	f	172	1
1371	1	Messengers aren't punished for bad news	f	172	1
1372	1	Other–I'll expain futher in the text box	t	172	1
1373	2	Flagging problems is discouraged here	f	172	1
1374	2	My manager ignores issues I raise	f	172	1
1375	2	People who raise problems get blamed	f	172	1
1376	2	Other–I'll expain futher in the text box	t	172	1
1377	1	My workload matches my capacity	f	173	1
1378	1	My manager balances my workload with me	f	173	1
1379	1	We prioritise so the load stays realistic	f	173	1
1380	1	Other–I'll expain futher in the text box	t	173	1
1381	2	My workload far exceeds my capacity	f	173	1
1382	2	My manager keeps piling work on	f	173	1
1383	2	Nothing gets de-prioritised so it just stacks up	f	173	1
1384	2	Other–I'll expain futher in the text box	t	173	1
1385	1	I make a point of noting my progress	f	174	1
1386	1	My manager helps me see how far I've come	f	174	1
1387	1	We review progress together as a team	f	174	1
1388	1	Other–I'll expain futher in the text box	t	174	1
1389	2	I never pause to notice progress	f	174	1
1390	2	My manager doesn't acknowledge progress	f	174	1
1391	2	My team never looks back at how far we've come	f	174	1
1392	2	Other–I'll expain futher in the text box	t	174	1
1393	1	My 1:1s end with clear agreed actions	f	175	1
1394	1	We capture next steps every meeting	f	175	1
1395	1	My manager follows up on what we agreed	f	175	1
1396	1	Other–I'll expain futher in the text box	t	175	1
1397	2	My 1:1s end without clear next steps	f	175	1
1398	2	We never capture actions	f	175	1
1399	2	Agreed actions are never followed up	f	175	1
1400	2	Other–I'll expain futher in the text box	t	175	1
1401	1	Learning resources are easy to access	f	176	1
1402	1	I have a budget to buy what I need	f	176	1
1403	1	My manager unblocks access when I ask	f	176	1
1404	1	Other–I'll expain futher in the text box	t	176	1
1405	2	Learning resources are hard to reach	f	176	1
1406	2	There's no budget for learning materials	f	176	1
1407	2	Getting access to resources is a hassle	f	176	1
1408	2	Other–I'll expain futher in the text box	t	176	1
1409	1	My scope expands as I prove myself	f	177	1
1410	1	My manager delegates more over time	f	177	1
1411	1	There's room to grow my responsibilities	f	177	1
1412	1	Other–I'll expain futher in the text box	t	177	1
1413	2	My responsibilities are stuck where they are	f	177	1
1414	2	My manager holds onto everything	f	177	1
1415	2	There's no room to take on more	f	177	1
1416	2	Other–I'll expain futher in the text box	t	177	1
1417	1	Leaders respond to honesty with openness	f	178	1
1418	1	Candour is welcomed even when critical	f	178	1
1419	1	Being honest with leaders has no downside	f	178	1
1420	1	Other–I'll expain futher in the text box	t	178	1
1421	2	Leaders get defensive when challenged	f	178	1
1422	2	Honest views are unwelcome at the top	f	178	1
1423	2	Speaking frankly to leaders backfires	f	178	1
1424	2	Other–I'll expain futher in the text box	t	178	1
1433	1	I deliberately pull lessons from wins and misses	f	180	1
1434	1	My manager debriefs outcomes with me	f	180	1
1435	1	My team runs retros to capture lessons	f	180	1
1436	1	Other–I'll expain futher in the text box	t	180	1
1437	2	I don't get time to extract lessons	f	180	1
1438	2	My manager never debriefs with me	f	180	1
1439	2	My team repeats mistakes without reviewing	f	180	1
1440	2	Other–I'll expain futher in the text box	t	180	1
1449	1	We treat struggles as learning opportunities	f	182	1
1450	1	My manager coaches me through tough spots	f	182	1
1451	1	I get support to turn setbacks into lessons	f	182	1
1452	1	Other–I'll expain futher in the text box	t	182	1
1453	2	Struggles just leave me stuck and frustrated	f	182	1
1454	2	My manager doesn't help me learn from them	f	182	1
1455	2	There's no support to recover and learn	f	182	1
1456	2	Other–I'll expain futher in the text box	t	182	1
1465	1	My team makes it safe to raise concerns	f	184	1
1466	1	We have norms that welcome dissent	f	184	1
1467	1	My manager sets a tone of openness	f	184	1
1468	1	Other–I'll expain futher in the text box	t	184	1
1469	2	Raising concerns with my team feels risky	f	184	1
1470	2	My team avoids difficult conversations	f	184	1
1471	2	My manager discourages raising concerns	f	184	1
1472	2	Other–I'll expain futher in the text box	t	184	1
1481	1	My manager schedules time to reflect	f	186	1
1482	1	My team has a regular reflection ritual	f	186	1
1483	1	Reflection time is protected rather than squeezed	f	186	1
1484	1	Other–I'll expain futher in the text box	t	186	1
1485	2	My manager never makes time to reflect	f	186	1
1486	2	My team has no reflection ritual	f	186	1
1487	2	Reflection always gets squeezed out	f	186	1
1488	2	Other–I'll expain futher in the text box	t	186	1
1497	1	I'm given time to build deeper expertise	f	188	1
1498	1	My manager sponsors my development in new areas	f	188	1
1499	1	I have mentors to help me specialise	f	188	1
1500	1	Other–I'll expain futher in the text box	t	188	1
1501	2	There's no time to build expertise	f	188	1
1502	2	My manager doesn't back my development	f	188	1
1503	2	I've no mentor to help me grow expertise	f	188	1
1504	2	Other–I'll expain futher in the text box	t	188	1
1513	1	People respond with empathy when things go wrong	f	190	1
1514	1	Leaders model compassion under pressure	f	190	1
1515	1	My manager is supportive when I struggle	f	190	1
1516	1	Other–I'll expain futher in the text box	t	190	1
1517	2	Mistakes are met with blame rather than empathy	f	190	1
1518	2	Leaders show no compassion when it's hard	f	190	1
1519	2	My manager is cold when things go wrong	f	190	1
1520	2	Other–I'll expain futher in the text box	t	190	1
1529	1	My team reflects on a regular schedule	f	192	1
1530	1	Reflection is an established team habit	f	192	1
1531	1	We protect time for team reflection	f	192	1
1532	1	Other–I'll expain futher in the text box	t	192	1
1533	2	My team rarely reflects together	f	192	1
1534	2	Reflection isn't a habit for us	f	192	1
1535	2	We never protect time to reflect	f	192	1
1536	2	Other–I'll expain futher in the text box	t	192	1
1545	1	Learning is built into how we work	f	194	1
1546	1	My manager builds learning into my tasks	f	194	1
1547	1	I learn on the job as a matter of course	f	194	1
1548	1	Other–I'll expain futher in the text box	t	194	1
1549	2	Learning only happens if I find spare time	f	194	1
1550	2	My manager treats learning as optional	f	194	1
1551	2	There's no room to learn day to day	f	194	1
1552	2	Other–I'll expain futher in the text box	t	194	1
1561	1	My team rallies round when I hit a setback	f	196	1
1562	1	My manager backs me through tough patches	f	196	1
1563	1	There's real support to help me recover	f	196	1
1564	1	Other–I'll expain futher in the text box	t	196	1
1565	2	I'm left alone to handle setbacks	f	196	1
1566	2	My manager withdraws when I struggle	f	196	1
1567	2	There's no support when things go wrong	f	196	1
1568	2	Other–I'll expain futher in the text box	t	196	1
1577	1	We have a forum to discuss how we work	f	198	1
1578	1	Our retros surface ways to improve	f	198	1
1579	1	My manager prompts us to improve our methods	f	198	1
1425	1	My priorities are clear enough to act on	f	179	1
1426	1	My manager helps me decide what comes first	f	179	1
1427	1	Demands are realistic enough to prioritise	f	179	1
1428	1	Other–I'll expain futher in the text box	t	179	1
1429	2	Everything is labelled top priority	f	179	1
1430	2	My manager won't help me prioritise	f	179	1
1431	2	Conflicting demands make focus impossible	f	179	1
1432	2	Other–I'll expain futher in the text box	t	179	1
1441	1	Our meetings are facilitated so all can speak	f	181	1
1442	1	We have norms that invite quieter voices	f	181	1
1443	1	Whoever chairs makes space for everyone	f	181	1
1444	1	Other–I'll expain futher in the text box	t	181	1
1445	2	A few voices dominate our meetings	f	181	1
1446	2	There's no effort to include quieter people	f	181	1
1447	2	Meetings run with no real facilitation	f	181	1
1448	2	Other–I'll expain futher in the text box	t	181	1
1457	1	I'm given time to develop my ideas	f	183	1
1458	1	My manager backs me to try ideas out	f	183	1
1459	1	We have a forum to explore new ideas	f	183	1
1460	1	Other–I'll expain futher in the text box	t	183	1
1461	2	There's no time to develop ideas	f	183	1
1462	2	My manager shuts ideas down fast	f	183	1
1463	2	Ideas have nowhere to go here	f	183	1
1464	2	Other–I'll expain futher in the text box	t	183	1
1473	1	Our systems are well-integrated and smooth	f	185	1
1474	1	Systems are reliable and rarely down	f	185	1
1475	1	I rarely need manual workarounds	f	185	1
1476	1	Other–I'll expain futher in the text box	t	185	1
1477	2	Disconnected systems stall my work	f	185	1
1478	2	Systems are often down or slow	f	185	1
1479	2	I waste time on manual workarounds	f	185	1
1480	2	Other–I'll expain futher in the text box	t	185	1
1489	1	Changes are announced with plenty of notice	f	187	1
1490	1	My manager forewarns me of what's coming	f	187	1
1491	1	We get lead time to plan for changes	f	187	1
1492	1	Other–I'll expain futher in the text box	t	187	1
1493	2	Changes are sprung with no notice	f	187	1
1494	2	My manager doesn't forewarn me	f	187	1
1495	2	There's never lead time to prepare	f	187	1
1496	2	Other–I'll expain futher in the text box	t	187	1
1505	1	I'm trusted with high-stakes work	f	189	1
1506	1	My manager delegates important work to me	f	189	1
1507	1	I'm given real ownership of what matters	f	189	1
1508	1	Other–I'll expain futher in the text box	t	189	1
1509	2	I'm only given low-stakes tasks	f	189	1
1510	2	My manager keeps important work to themselves	f	189	1
1511	2	I'm never trusted with anything that matters	f	189	1
1512	2	Other–I'll expain futher in the text box	t	189	1
1521	1	There's a clear path to escalate blockers	f	191	1
1522	1	My manager clears blockers quickly	f	191	1
1523	1	Support responds fast when I'm stuck	f	191	1
1524	1	Other–I'll expain futher in the text box	t	191	1
1525	2	Blockers have no clear escalation route	f	191	1
1526	2	My manager is slow to clear blockers	f	191	1
1527	2	Support takes ages when I'm stuck	f	191	1
1528	2	Other–I'll expain futher in the text box	t	191	1
1537	1	Leaders share the reasoning and not just the call	f	193	1
1538	1	My manager walks me through the thinking	f	193	1
1539	1	Decision rationale is communicated openly	f	193	1
1540	1	Other–I'll expain futher in the text box	t	193	1
1541	2	We're told decisions but never the why	f	193	1
1542	2	My manager can't explain the reasoning	f	193	1
1543	2	The thinking stays hidden from us	f	193	1
1544	2	Other–I'll expain futher in the text box	t	193	1
1553	1	I can see opportunities across the organisation	f	195	1
1554	1	My manager exposes me to wider opportunities	f	195	1
1555	1	I have networks that open up new paths	f	195	1
1556	1	Other–I'll expain futher in the text box	t	195	1
1557	2	I see nothing beyond my current role	f	195	1
1558	2	My manager keeps me boxed in	f	195	1
1559	2	Wider opportunities aren't visible to me	f	195	1
1560	2	Other–I'll expain futher in the text box	t	195	1
1569	1	We have dedicated 'no meeting' times	f	197	1
1570	1	I review priorities and workload with my manager	f	197	1
1571	1	I block focus time in my diary	f	197	1
1572	1	Other–I'll expain futher in the text box	t	197	1
1573	2	Back-to-back meetings leave no focus time	f	197	1
1574	2	My priorities aren't reviewed with my manager	f	197	1
1575	2	I can't protect focus time in my diary	f	197	1
1576	2	Other–I'll expain futher in the text box	t	197	1
1585	1	We have ways to recognise good work	f	199	1
1586	1	My manager regularly praises my contribution	f	199	1
1580	1	Other–I'll expain futher in the text box	t	198	1
1581	2	We never discuss how to work better	f	198	1
1582	2	We hold no retros or reviews	f	198	1
1583	2	My manager never raises improvement	f	198	1
1584	2	Other–I'll expain futher in the text box	t	198	1
1593	1	I'm given time to consolidate new learning	f	200	1
1594	1	My manager allows time to embed skills	f	200	1
1595	1	Our pace lets learning actually sink in	f	200	1
1596	1	Other–I'll expain futher in the text box	t	200	1
1597	2	I move on before learning sticks	f	200	1
1598	2	My manager rushes me to the next thing	f	200	1
1599	2	The pace leaves no time to absorb	f	200	1
1600	2	Other–I'll expain futher in the text box	t	200	1
1609	1	Saying 'I don't know' is accepted here	f	202	1
1610	1	Leaders openly admit when they're unsure	f	202	1
1611	1	My manager makes uncertainty safe	f	202	1
1612	1	Other–I'll expain futher in the text box	t	202	1
1613	2	I feel I must always seem certain	f	202	1
1614	2	No one senior ever admits uncertainty	f	202	1
1615	2	Admitting I'm unsure feels judged	f	202	1
1616	2	Other–I'll expain futher in the text box	t	202	1
1625	1	I talk reflections through with colleagues	f	204	1
1626	1	We have a forum to share what we've learned	f	204	1
1627	1	Sharing lessons with peers is part of our culture	f	204	1
1628	1	Other–I'll expain futher in the text box	t	204	1
1629	2	I only ever reflect alone	f	204	1
1630	2	We have no forum to share reflections	f	204	1
1631	2	Comparing lessons just isn't done here	f	204	1
1632	2	Other–I'll expain futher in the text box	t	204	1
1641	1	My skills are visibly progressing in this role	f	206	1
1642	1	My manager tracks and supports my growth	f	206	1
1643	1	My role keeps stretching my abilities	f	206	1
1644	1	Other–I'll expain futher in the text box	t	206	1
1645	2	My skills have stalled in this role	f	206	1
1646	2	My manager doesn't track my growth	f	206	1
1647	2	My role no longer stretches me	f	206	1
1648	2	Other–I'll expain futher in the text box	t	206	1
1587	1	Peers recognise each other's work	f	199	1
1588	1	Other–I'll expain futher in the text box	t	199	1
1589	2	Good work goes unrecognised here	f	199	1
1590	2	My manager rarely praises anyone	f	199	1
1591	2	There's no peer recognition culture	f	199	1
1592	2	Other–I'll expain futher in the text box	t	199	1
1601	1	I can see the impact my work has	f	201	1
1602	1	My manager shows me the results of my work	f	201	1
1603	1	I get feedback on the difference I make	f	201	1
1604	1	Other–I'll expain futher in the text box	t	201	1
1605	2	I never see any impact from my work	f	201	1
1606	2	My manager never shares results	f	201	1
1607	2	I get no feedback on my impact	f	201	1
1608	2	Other–I'll expain futher in the text box	t	201	1
1617	1	Obstacles get tackled quickly here	f	203	1
1618	1	My manager prioritises clearing obstacles	f	203	1
1619	1	There's clear ownership for fixing issues	f	203	1
1620	1	Other–I'll expain futher in the text box	t	203	1
1621	2	Obstacles drag on without resolution	f	203	1
1622	2	My manager is slow to act on issues	f	203	1
1623	2	No one owns fixing the problems	f	203	1
1624	2	Other–I'll expain futher in the text box	t	203	1
1633	1	People genuinely listen when I talk	f	205	1
1634	1	My manager gives me their full attention	f	205	1
1635	1	We don't talk over each other here	f	205	1
1636	1	Other–I'll expain futher in the text box	t	205	1
1637	2	People wait to talk rather than listen	f	205	1
1638	2	My manager is distracted in our conversations	f	205	1
1639	2	I get talked over in discussions	f	205	1
1640	2	Other–I'll expain futher in the text box	t	205	1
1649	1	Internal moves are actively supported	f	207	1
1650	1	My manager is open to me exploring new paths	f	207	1
1651	1	Pathways across the company are visible	f	207	1
1652	1	Other–I'll expain futher in the text box	t	207	1
1653	2	Internal moves are quietly discouraged	f	207	1
1654	2	My manager wants to keep me where I am	f	207	1
1655	2	New paths inside the company aren't visible	f	207	1
1656	2	Other–I'll expain futher in the text box	t	207	1
703	1	This is a genuinely psychologically safe place	f	118	1
704	1	My manager creates a safe environment	f	118	1
705	1	I can show up without fear of judgement	f	118	1
706	2	I'm often on edge at work	f	118	1
707	2	My manager creates a tense atmosphere	f	118	1
708	2	I feel I have to protect myself here	f	118	1
1657	1	This is a genuinely psychologically safe place	f	208	1
1658	1	My manager creates a safe environment	f	208	1
1659	1	I can show up without fear of judgement	f	208	1
1660	1	Other–I'll expain futher in the text box	t	208	1
1661	2	I'm often on edge at work	f	208	1
1662	2	My manager creates a tense atmosphere	f	208	1
1663	2	I feel I have to protect myself here	f	208	1
1664	2	Other–I'll expain futher in the text box	t	208	1
1665	1	Our ways of working set me up to do great work	f	209	1
1666	1	My manager removes friction so I can deliver	f	209	1
1667	1	We continuously improve how we work	f	209	1
1668	1	Other–I'll expain futher in the text box	t	209	1
1669	2	Our ways of working undermine quality	f	209	1
1670	2	My manager doesn't tackle the friction	f	209	1
1671	2	We never improve how we work	f	209	1
1672	2	Other–I'll expain futher in the text box	t	209	1
1673	1	I make sure I implement improvement actions	f	210	1
1674	1	I agree on improvements with my manager	f	210	1
1675	1	We prioritise and action improvements as a team	f	210	1
1676	1	Other–I'll expain futher in the text box	t	210	1
1677	2	My reflections rarely lead to any action	f	210	1
1678	2	My manager and I don't agree on improvements	f	210	1
1679	2	My team never actions the improvements we spot	f	210	1
1680	2	Other–I'll expain futher in the text box	t	210	1
\.


--
-- Data for Name: DailyMicroActions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DailyMicroActions" ("Id", "Text", "ContentCultureId", "ReasonId") FROM stdin;
1	Sorry that people seemed distracted! How about a quick follow-up chat with one person? Keep it simple and highlight your key point clearly.	1	1
2	It’s tough when your points are missed. Send a friendly, short message restating your key idea—clarifying it helps everyone.	1	2
3	If misunderstandings are lingering, gently ask, “Can we quickly clarify this?” One simple question can clear things up nicely.	1	3
4	That's wonderful! Quickly thank whoever listened so attentively—your appreciation encourages continued positive interactions.	1	4
5	Great to hear your ideas sparked discussion! Send a quick note thanking your team—it keeps good conversations flowing.	1	5
6	Fantastic! Reinforce this by acknowledging your team’s clear communication in a brief message or chat.	1	6
7	Routine challenges can feel dull. Try asking someone for a fresh idea or take a few minutes exploring a new angle—it helps refresh your approach.	1	7
8	Feeling overwhelmed is tough. Pause briefly, jot down your top three tasks, and tackle one small, manageable task first.	1	8
9	Reflection really helps growth. Take two minutes now to note one insight or lesson from today—it’ll make a real difference tomorrow.	1	9
10	That’s a win! Quickly jot down the skill you improved today or share it with someone—it feels great to celebrate your growth.	1	10
11	Awesome! Reinforce your learning by quickly noting down or sharing one important lesson from today.	1	11
12	Great alignment! Tell your mentor or manager how today's challenges supported your goals—it helps ensure you keep getting tasks that inspire growth.	1	12
13	No opportunities today? Why not ask your manager or a colleague if there's something small and new you could try? It’s an easy way to keep growing!	1	13
14	It can be tough to find time to learn. Schedule just five minutes tomorrow for exploring something new—even a small step counts!	1	14
15	If nothing’s being shared, gently ask HR or your manager for quick learning resources—a friendly email could open up great opportunities!	1	15
16	Fantastic job on engaging with new experiences! Make sure to jot down or share one thing you learned—celebrate your growth!	1	16
17	Awesome! Take five minutes now to explore one resource you haven’t tried yet—you never know what helpful tip you'll discover.	1	17
18	It’s great when leaders support learning! Quickly thank your manager—it reinforces their encouragement for you and others.	1	18
19	Feeling left out is tough. Try inviting someone quieter to chat briefly—small conversations help build a warmer group dynamic.	1	19
20	Understandable to feel anxious! Prepare one quick idea or question before the next meeting—it’ll boost your confidence.	1	20
21	If openness feels lacking, gently suggest a quick round for everyone to share one thought—it helps conversations feel more inclusive.	1	21
22	That's fantastic! Why not share quick gratitude with your team for creating a welcoming space—it helps sustain that positive environment.	1	22
23	Wonderful! Quickly acknowledge the friendly teamwork—sharing your appreciation encourages everyone to continue being supportive.	1	23
24	Terrific! Let the group know their clear, respectful communication made a difference—it reinforces positive behavior for next time.	1	24
25	Outdated tools are frustrating! Quickly log a request with IT—it’s a small step that helps improve things for everyone.	1	25
26	Feeling slowed down by a tool? Ask a teammate for a quick tip or shortcut—it’s often the quickest fix!	1	26
27	Integration issues can be annoying. Share one specific example with IT or your manager to start resolving the problem.	1	27
28	Fantastic! A quick message thanking IT for the effective tools is always appreciated and helps keep good things coming.	1	28
29	Efficiency boost—nice! Share how the tools helped speed up your tasks; it’s a friendly way to encourage others to use them too.	1	29
30	Smooth technology experiences are great! Send a quick thank-you to IT or management—it highlights how valuable good tools are.	1	30
31	It’s hard to pause when overwhelmed. Try setting a two-minute timer before finishing today, jotting down one quick thought—it’ll help you reset.	1	31
32	Reflection matters! Schedule a recurring three-minute slot each day—it’s short enough to manage and still very effective.	1	32
33	Reflection can feel tricky! Try a simple question like, “What’s one thing I did well today?” Answering just that helps get you started.	1	33
34	Fantastic commitment! Keep protecting your reflection slot—maybe even set a quick reminder to make sure it stays a priority.	1	34
35	That’s great insight! Why not share a reflection tip with a coworker—it's an easy way to encourage their growth, too.	1	35
36	Wonderful habit! Try quickly teaching a teammate your method—it can help reinforce your practice and encourage others.	1	36
37	It's understandable to feel hesitant. Try sharing a small, simple suggestion first—starting small helps build your confidence.	1	37
38	It’s tough if the environment doesn’t feel welcoming. Consider sharing feedback privately with one supportive colleague first—a friendly ally can help.	1	38
39	Past experiences can be tough to overcome. Think about one positive feedback experience you’ve had to encourage yourself before speaking up again.	1	39
40	That's excellent! Reinforce that safe feeling by quickly thanking the team or your manager for encouraging open dialogue.	1	40
41	Love hearing that! Help keep this going by gently encouraging quieter team members to also share their thoughts next time.	1	41
42	Great experience! Keep the momentum going by quickly giving someone else positive reinforcement for their feedback today.	1	42
43	Insufficient resources can be tough. Send a quick message to your manager asking if additional resources or alternatives are available—it’s a simple first step.	1	43
44	Feeling unsupported is difficult. Reach out to a trusted colleague with a specific question—sometimes initiating a small conversation opens doors.	1	44
45	Feeling unprepared? Quickly ask a colleague who's done it before for a checklist or quick overview—it’ll help build your confidence.	1	45
46	Awesome! Why not spend a couple of minutes exploring one advanced feature to fully leverage your available tools?	1	46
47	Fantastic! Quickly thank a teammate or manager who provided support today—gratitude encourages continued helpfulness.	1	47
48	Clear resources make life easier! Quickly let the training creator know their materials were helpful—everyone appreciates positive feedback.	1	48
49	That’s tough! Start small—suggest a tiny, low-risk idea in your next meeting. Even small steps can gradually encourage more openness.	1	49
50	No direct encouragement today? Try gently asking your manager if you could test a small, safe experiment—it could open doors to more support!	1	50
51	Fear of failing can hold you back. Remind yourself it's okay to experiment—try one small change in how you approach a task today, just to see what happens!	1	51
52	That's wonderful! Quickly acknowledge this support with your manager or team—it reinforces that taking thoughtful risks is valuable.	1	52
53	Fantastic team culture! Reinforce it by thanking your team for their openness today—positive feedback keeps everyone feeling encouraged.	1	53
54	Great news! Take a moment to thank your manager for providing these growth opportunities—it helps maintain their support for future endeavors.	1	54
55	Feeling unsupported is tough. Try briefly chatting with a colleague or sending a quick message clearly stating what help you need—it could make a big difference.	1	55
56	If support missed the mark, try clarifying exactly what you need next time with a friendly request—specific requests can lead to better outcomes.	1	56
57	Competition is tough. Offer help on something small to a teammate—friendly gestures often foster a warmer, more supportive environment.	1	57
58	Wonderful! Quickly thank your team for their timely help today—everyone appreciates hearing their support made a difference.	1	58
59	Fantastic! Let your team know how much their support meant to you today—it helps keep that positive energy flowing.	1	59
60	That’s great initiative from your team! Consider acknowledging their proactive help—it encourages them to keep looking out for each other.	1	60
61	It's tough when things feel restrictive. Try suggesting a small tweak in your approach next time—just a friendly request might open more flexibility.	1	61
62	Frustration with inefficiency is understandable. Identify one small step you could simplify and gently propose the change—it could make your workflow smoother.	1	62
63	Everyone works differently! Maybe ask your manager if you can briefly test a method that suits you better—even small experiments help demonstrate effectiveness.	1	63
64	Fantastic! Share with your manager that these methods work great for you—it helps ensure you keep working in a way that suits your strengths.	1	64
65	Well done! Take a moment to note down or share what made today’s methods successful—it reinforces what’s working for you.	1	65
66	Great news! Quickly jot down the creative approach you used today—it’s helpful for future reference and celebrating your innovative spirit.	1	66
67	Missed reflections happen! Tomorrow morning, briefly ask your team for a quick recap—it keeps everyone learning and growing.	1	67
68	Learning from outcomes can feel tricky. Ask a teammate for their approach—one quick conversation might spark useful ideas!	1	68
69	Reflection can be challenging! Try answering a simple question like, “What’s one small thing I can improve tomorrow?” It’s a gentle start.	1	69
70	That’s great! Quickly note down one actionable insight you gained today—it reinforces learning and helps guide your next steps.	1	70
71	Excellent! Sharing briefly with your manager or mentor how today’s outcomes fit your goals helps keep you focused and motivated.	1	71
72	Wonderful support! Quickly thank whoever encouraged your reflection today—it promotes a supportive and thoughtful environment.	1	72
73	Ambiguity can be frustrating! Consider gently asking the sender for a quick clarification—it helps everyone stay on track.	1	73
74	Direct communication makes life easier. Try summarizing your understanding quickly to the team—it gently prompts clarity without pressure.	1	74
75	Fragmented info is tough! Send a quick, friendly message asking teammates to clarify or fill in gaps—it’s a simple way to improve clarity.	1	75
76	Fantastic! Let your colleagues know their clarity made your day easier—it encourages them to keep communicating clearly.	1	76
77	That’s great to hear! Quickly thank the sender—it reinforces their helpfulness and ensures good communication continues.	1	77
78	Excellent teamwork! Mention briefly in your next interaction how valuable their efforts for mutual understanding were—it encourages continued clarity.	1	78
79	It's natural to worry, but consider privately sharing one small mistake with a trusted colleague first—it’s a gentle step towards feeling safer.	1	79
80	Missed opportunities happen! Suggest adding a quick, informal 'what we learned' moment to your next meeting—it helps normalize learning from errors.	1	80
81	Tough environments can discourage openness. Reflect privately on one small mistake, noting what you learned—it's safe and valuable personal growth.	1	81
82	Fantastic! Keep this positive habit going—briefly share one lesson learned from a mistake next time to encourage openness.	1	82
83	Great culture! Support it further by gently acknowledging someone else's openness—it's a small way to strengthen your team's positive outlook.	1	83
84	Wonderful! Take a moment to quickly thank the person who supported you—it reinforces a culture of helpfulness and openness.	1	84
85	It’s tough when your hard work isn’t seen. Share a small achievement with a trusted teammate—it’s important to acknowledge your wins!	1	85
86	General feedback can feel unhelpful. Politely ask for one specific thing you did well—it helps clarify your impact.	1	86
87	That can be discouraging! Make a quick note of your successes and gently highlight one in your next update—it keeps your efforts visible.	1	87
88	Awesome! Keep the positivity flowing—quickly recognize a teammate’s effort today too; it builds a great atmosphere.	1	88
89	That’s fantastic! Consider letting the person know their recognition really mattered—it encourages continued appreciation.	1	89
90	Wonderful work! Quickly jot down what you did well today—it’s rewarding to recognize your own achievements.	1	90
91	Totally understandable to feel cautious. Start by sharing one small frustration privately with someone supportive—it helps build comfort.	1	91
92	It's challenging when emotions can't be shared openly. Consider briefly writing out frustrations privately—it’s a safe first step to acknowledging feelings.	1	92
93	Negative past experiences are tough. Remind yourself of one positive time you spoke up—it can gently rebuild your confidence.	1	93
94	Fantastic! Quickly thank whoever made you feel safe—it reinforces the positive environment you’re experiencing.	1	94
95	Great environment! Try voicing one small, constructive frustration—it’s a simple step that keeps communication open and healthy.	1	95
96	Awesome! Make time today for a quick stress-relief exercise provided by your workplace—it’s an easy win for well-being.	1	96
97	It’s challenging when autonomy feels limited. Consider politely asking your manager if you can handle a small decision independently—it can gradually build trust.	1	97
98	Strict rules can be tough. Maybe suggest a small flexibility in how you complete a task next time—a gentle conversation might open possibilities.	1	98
99	Lack of trust feels difficult. Offer your manager a quick update proactively on a task you’re managing—it gently demonstrates your capability and can build trust.	1	99
100	Fantastic! Keep making those decisions confidently—maybe share briefly how autonomy helped you succeed today with your team or manager.	1	100
101	That’s excellent! Use this freedom today to try one new technique or approach—it’s a great chance to discover something helpful or enjoyable.	1	101
102	Great to have trust! Send your manager a quick update on something you’ve completed independently—it reinforces their trust in you.	1	102
103	It's frustrating to miss out on feedback. Gently ask a colleague or supervisor for quick, specific input on a recent task—it’s a useful step forward.	1	103
104	Critical feedback can feel tough. Politely ask for one constructive suggestion on how to improve—it transforms criticism into actionable insights.	1	104
105	Late feedback isn’t helpful. Next time you finish a task, ask immediately for quick feedback—it helps everyone stay timely and aligned.	1	105
106	Awesome! Quickly act on one piece of feedback or at least jot down how you'll apply it—it makes the most of helpful input.	1	106
107	Great! Quickly thank the feedback giver, mentioning how their clarity helped you—it encourages continued helpful feedback.	1	107
108	Great to hear! Pass the positivity on by giving quick, positive feedback to a teammate—it keeps the good vibes going.	1	108
109	It's tough when others seem dismissive. Respond calmly with a polite question—it can gently steer the interaction back to a positive path.	1	109
110	Unfocused meetings can be draining. Next time, try suggesting a quick agenda—it gently helps everyone stay productive and engaged.	1	110
111	Feeling undervalued is hard. Consider privately mentioning how you felt to a trusted colleague or manager—it’s a gentle way to address and resolve concerns.	1	111
112	Fantastic! Let your colleagues know briefly that their constructive approach was appreciated—it encourages continued positive interactions.	1	112
113	Excellent! Quickly acknowledge your team’s respectful approach—it helps sustain this great dynamic going forward.	1	113
114	Great teamwork! Take a moment to briefly thank your teammates—it helps reinforce positive behaviors and boosts morale.	1	114
115	Personal development can slip when busy. Schedule a quick 5-minute learning activity tomorrow—even a small commitment can spark growth.	1	115
116	If you’re not seeing opportunities, gently ask your manager or mentor for a quick recommendation on something small to try—it’s an easy first step.	1	116
117	It’s understandable to question relevance. Try setting a specific small goal for one activity next time—focusing on one skill can highlight its value clearly.	1	117
118	Great commitment! Briefly share one insight or achievement from your self-improvement activity—it helps reinforce your progress.	1	118
119	Excellent alignment! Consider briefly mentioning to your mentor or manager how your development activities support your career goals—it reinforces your path forward.	1	119
120	That's fantastic motivation! Quickly note your progress in a journal or briefly share it with someone—it helps sustain your enthusiasm and growth.	1	120
121	That sounds frustrating! Consider reaching out with a quick message to someone from another team—starting small can break down silos.	1	121
122	It's tough feeling disconnected. Quickly invite a colleague for a brief chat or virtual coffee—it’s an easy step toward stronger connections.	1	122
123	Remote collaboration has challenges! Schedule a brief, casual check-in call—it can help build rapport despite distance.	1	123
124	Excellent teamwork! Quickly thank your team for effective communication—it reinforces the good habits that made today smooth.	1	124
125	Great to hear! Consider quickly acknowledging the IT team—it helps ensure you keep getting helpful tools.	1	125
126	Wonderful! Let your teammates know briefly how much their support meant—it reinforces the strong community you’ve built together.	1	126
127	Fear of blame is understandable. Start gently by privately sharing a small setback with a trusted colleague—it can slowly build your comfort level.	1	127
128	Safe spaces matter! Consider proposing a quick, casual 'lessons learned' moment in your next meeting—it can help create a safer atmosphere.	1	128
129	Tough situations are difficult if ignored. Privately jot down one lesson from a recent failure—recognizing it yourself is a safe first step.	1	129
130	Great environment! Quickly share your own learning from a setback—it encourages everyone to openly learn and grow together.	1	130
131	Fantastic! Quickly thank your colleagues or manager for their supportive approach—it reinforces the open culture you value.	1	131
132	Excellent leadership! Briefly thank your manager—it helps ensure continued transparency and support.	1	132
133	It’s challenging when support is hard to reach. If one channel fails, try quickly messaging through another—sometimes a different route makes all the difference!	1	133
134	Unhelpful assistance can be frustrating! Next time, try clearly specifying one detail you need help with—it gently guides supporters to the right solution.	1	134
135	Feeling isolated is tough. Quickly reach out to one trusted colleague—even a short conversation can significantly ease that feeling of isolation.	1	135
136	Fantastic! Take a moment to thank the person who supported you today—a quick note of appreciation reinforces their helpfulness.	1	136
137	Awesome! Let your manager or colleague know briefly how helpful their quick support was—it encourages them to keep providing effective guidance.	1	137
138	Wonderful feeling! Consider quickly offering help to someone else today—it keeps the supportive atmosphere thriving.	1	138
139	It's tough when your achievements are overlooked. Briefly share one success with a friendly colleague—it's good to acknowledge your efforts!	1	139
140	If there isn’t a platform, start small by informally sharing one quick win with your team—it gently encourages others to do the same.	1	140
141	Celebrating small wins matters! Quickly mention one small achievement to a teammate—it might inspire others to celebrate too.	1	141
142	Great habit! Keep noting small wins each day—it feels good and helps you remember your achievements.	1	142
143	Wonderful team spirit! Quickly thank teammates for celebrating together—it reinforces a positive culture for everyone.	1	143
144	Fantastic! Quickly congratulate someone else on their achievement today—it helps keep that positive atmosphere strong.	1	144
145	Asking for help can feel risky! Try asking a small question to a trusted teammate first—it gently builds your confidence in opening up.	1	145
146	Negative responses are tough. Try reaching out to someone new or asking your question in a different way—it’s a simple shift that might yield better support.	1	146
147	It’s hard when support seems out of reach. Start by briefly checking in with a trusted colleague—they can often point you in the right direction.	1	147
148	Fantastic! Quickly thank whoever provided supportive help—it encourages more of this valuable behavior.	1	148
149	Wonderful to hear! Consider briefly encouraging someone else today—passing it forward keeps the positivity flowing.	1	149
150	Great news! Use this supportive environment to ask another question if you’re stuck—it’s a perfect opportunity to continue feeling safe and supported.	1	150
151	Sparse feedback can leave you unsure. Consider politely requesting quick, constructive feedback on one specific area—clarity helps you improve.	1	151
152	Negative feedback without guidance is tough. Gently ask for one positive suggestion for improvement—it turns criticism into helpful insight.	1	152
153	Late feedback is frustrating! Next time you finish a task, quickly request immediate input—it gently nudges timely feedback in the future.	1	153
154	Awesome! Quickly thank the feedback giver and briefly note how you'll use their suggestions—it encourages continued quality feedback.	1	154
155	Excellent! Briefly share the improvement you made with your team or manager—it highlights how valuable timely feedback is.	1	155
156	Great insight! Consider briefly sharing what you learned with a colleague—it multiplies the value of your feedback.	1	156
157	It’s tough feeling disconnected. Take five minutes to list one small thing you can do to align tomorrow’s activities closer to your personal goals.	1	157
158	Feeling unclear is understandable. Consider briefly asking your manager or a mentor for one quick idea or step to align your tasks with your goals.	1	158
159	Feeling stuck is tough. Quickly research a small skill or activity you can pursue next—it’s a manageable step toward feeling back on track.	1	159
160	Fantastic alignment! Quickly acknowledge this progress to yourself or share briefly with a mentor—it reinforces your positive trajectory.	1	160
161	Great news! Take a moment to thank whoever’s guiding your path—it reinforces the valuable support you're receiving.	1	161
162	Wonderful to hear! Briefly volunteer or step into an opportunity that matches your ambitions—it helps you feel motivated and aligned.	1	162
163	Superficial interactions can be tough. Try briefly asking a colleague about their day—it gently shifts conversations to a more meaningful place.	1	163
164	It's challenging to build deeper connections. Schedule a quick chat with someone you’d like to know better—a small step to more meaningful conversations!	1	164
165	Time constraints make connection tough. Next time a meeting ends early, briefly ask a colleague about something non-work-related—it’s a quick way to build bonds.	1	165
166	Wonderful! Quickly follow up thanking your colleague—it’s a simple gesture that strengthens your relationship further.	1	166
167	Fantastic connection! Consider quickly scheduling another chat to continue building that valuable relationship.	1	167
168	Great connection! Share a relevant article or resource briefly—it’s an easy, thoughtful way to deepen your shared interest further.	1	168
169	Feeling overloaded is tough. Take a quick moment to identify one low-priority task you could delay or delegate—it's a small step towards balance.	1	169
170	Deadlines can feel overwhelming. Pause briefly for a simple breathing or stretch break—even two minutes can help reset your focus and ease pressure.	1	170
171	It’s challenging when breaks seem impossible. Set a reminder for a one-minute stretch or quick walk—tiny breaks can greatly improve your day.	1	171
172	Fantastic! Keep noting what made today balanced—briefly scheduling similar routines in the future ensures ongoing well-being.	1	172
173	Great to hear! Quickly thank your manager or planner for prioritizing your well-being—it reinforces their thoughtful approach.	1	173
174	Wonderful! Use some of this time today for quick self-care—a short walk, breathing exercise, or stretching can help you sustain this balance.	1	174
175	It’s easy to overlook personal growth when busy. Take a quick five minutes tomorrow to watch a short tutorial or read something insightful.	1	175
176	Reflection can feel new at first. Quickly note one thing you did well today and one area to improve—it gently builds your self-awareness habit.	1	176
177	Identifying strengths can be tricky! Gently ask a trusted colleague to briefly share one strength they see in you—it’s encouraging and insightful.	1	177
178	Excellent habit! Keep it going by quickly sharing an insight you discovered today—it helps reinforce your ongoing growth.	1	178
179	Great opportunity! Briefly choose one strength to build on and one weakness to gently address—clear goals boost motivation.	1	179
180	Awesome job reflecting! Quickly note down one small action to tackle an identified area tomorrow—it makes growth manageable and rewarding.	1	180
181	Set the tone with the most junior person in the room, Respect across all levels holds because people copy what they see, and the strongest signal is how senior people treat junior ones. Today, make a point of giving someone junior your full attention in a conversation, asking their view and crediting it. One visible act like that quietly tells everyone watching what normal looks like here.	1	181
182	Name the agreement before you raise the disagreement, Respectful and constructive is a fragile mix, and it usually breaks at the moment people disagree. Next time you push back on someone, say the part of their thinking you agree with first, in their words, then add your different view. Starting from common ground keeps the disagreement about the work rather than the person.	1	182
183	Start one good interaction rather than waiting for one, The positive interactions lifting your work do not have to be something that happens to you. Today, be the person who starts one: a genuine question about someone's weekend, noticing a piece of work you liked, a real thank you. The mood you are enjoying is partly made by people doing exactly this, so add to the supply.	1	183
184	Name the specific moment rather than carrying the feeling, Feeling undervalued in some exchanges is hard to act on because it builds up as a vague sense rather than a clear event. Pick the single clearest recent example. Next time something similar happens, name just that behaviour calmly in the moment: what was said and how it landed. A specific point is far easier to raise than a mood.	1	184
185	Lead with the exact response you need from them, Input is easiest to dismiss when it lands as an open comment with nothing to act on. Next time you take something to a manager who waves things off, open with the exact thing you need: a decision, a yes or no, an answer by a date. A clear ask is harder to brush past than a general point.	1	185
186	Ask what would make it easy for them to say yes, Obstruction usually protects something: a competing priority, or a worry about being blamed for the outcome. Pushing harder rarely moves it. Pick the person blocking you most and ask the question directly: what would make it easy for you to help with this? Their answer tells you the real obstacle, which is the only thing you can actually negotiate with.	1	186
187	Write the lesson down before it starts to feel obvious, A lesson learned from a task feels obvious right after you learn it, which is exactly why it disappears within a month. Take the most useful thing a recent task taught you and write it as one plain sentence somewhere you will see it again. The note is what turns a one-time experience into something you can deliberately repeat.	1	187
188	Tell your manager which tasks are growing you, Tasks that match where you want to go are more valuable than they look, and the match tends to drift as priorities shift. Tell your manager plainly which current work is building the skills you care about. Managers assign on rough guesses, so naming what is working makes it far more likely the next assignment lands in the same place.	1	188
189	Name the skill you gained beneath the problem you solved, After solving something hard it is easy to file it as a finished problem and move on. The more valuable thing is the skill underneath it. Spend two minutes naming what you can now do that you could not before. Stated in general terms, it becomes something you can reuse on the next problem and offer to a colleague stuck on a similar one.	1	189
190	Change how you do one routine task this week, Routine work stops teaching you anything when you run it on autopilot, but the task itself is not the only variable. Pick one routine task this week and change how you do it: a tighter time limit, a cleaner method, a small experiment. The learning comes from the new approach, and that part is yours to control even when the task is not.	1	190
191	Attach growth to a task you already have to do, When you are busy, anything labelled growth reads as extra and drops off the list. So stop treating it as separate. Take a task already on your plate this week and pick one part of it to do better than usual. No extra time, same work, done in a way that builds something. That is the only kind of growth that survives a busy week.	1	191
192	Ask one question at the end of your next hard task, Reflection feels like it needs a slot you do not have, when it really needs one question asked at the right moment. When you finish your next difficult task, before you move on, ask yourself one thing: what would I do differently next time? Write the answer in a sentence. Asked while the experience is fresh, that one question is most of the insight you are missing.	1	192
193	Show a teammate the link they might not see, Seeing how your work connects to the bigger goals is an advantage, and not everyone near you has it. Pick one teammate and in your next conversation point out how a piece of their work feeds something larger. People work with more purpose when they can see the line from their task to the goal, and right now you can draw it for them.	1	193
194	Capture what you did while the feedback is fresh, Great feedback feels good for a day and then blurs, and the useful part blurs with it. Before that happens, write down the specific thing you did to earn it, the choice or the extra effort that made the difference. That note does two jobs. It is something concrete to repeat, and it is evidence you can point to when you next need to make your case.	1	194
195	Work out what created the time and protect it, Getting real time on important work is rarer than it should be, and it usually happens because of a specific condition: a blocked calendar, a quiet morning, a delegated task. Notice which one it was. Then deliberately recreate it next week before the time gets filled by something louder. Protecting the condition is how you make that progress a pattern instead of a lucky week.	1	195
196	Claim 30 minutes for proactive work before the day starts, Reactive work expands to fill whatever attention you leave open, so the only proactive time you get is time you take first. Tomorrow, block the first thirty minutes for one thing that matters but is not urgent, and do it before opening email or messages. It will not clear the operational load, but it stops the load from taking every hour by default.	1	196
197	Question one recurring meeting instead of accepting all of them, Meetings fill a calendar because each one looks individually reasonable and nobody audits the total. Pick the single recurring meeting you get the least from. Ask the organiser honestly whether you are needed every time, or could read the notes instead. Worst case, nothing changes. Often they have wondered the same thing, and you have bought back an hour every week.	1	197
198	Put one piece of input in writing and follow it up, Spoken input is easy to lose because it leaves no trace and nobody is tracking it. Next time you suggest something that matters, put it in two written lines to the right person, and a few days later ask what they decided. This does not guarantee a yes, but it makes your input something visible that has to be answered rather than something that quietly evaporates.	1	198
199	Tell one teammate exactly how their help landed, Timely support keeps flowing when the people giving it know it mattered, and most thanks is too vague to do that job. Pick one teammate who helped you recently and tell them the specific difference it made: what you were stuck on, and what their help let you do. Precise thanks is more genuine, and it quietly makes them more likely to step in again.	1	199
200	Be the one who starts the non-work moment today, Camaraderie is not built in the work itself. It is built in the small moments around it, and those moments happen because someone starts them. Today, be that someone. Ask a real question, share something that is not a task, take two minutes that are not about output. The team feeling you are benefiting from is made of exactly these moments, so add one.	1	200
201	Spot someone's load and offer before they ask, Being helped before you have to ask is rare, and it only happens because someone is paying attention to your week. Return that this week. Look for one teammate who is visibly stretched and offer something specific before they raise it: take a task, cover a meeting, look something over. The culture you are benefiting from exists because people do this for each other.	1	201
202	Make your next ask small and time-boxed, A general appeal for help is easy for a busy team to let slide, because it is not clear what is being asked or how much it costs. Make your next request impossible to misread: name the exact thing, say how long it will take, give a day. People say yes far more readily to something small and defined than to an open need.	1	202
203	Hand them the outcome you need rather than the topic, Help misses when the person guesses at what you need from a vague description. The fix is on your side of the ask. Next time, do not just name the topic, name the outcome: what you want to be true afterwards, and what you have already tried. Given the real target, people aim at it. Given a topic, they aim at what they assume.	1	203
204	Make one cooperative move visible this week, Competitive environments hold partly because everyone is waiting to see if cooperating will be punished, so someone has to move first. This week, make one cooperative act visible: share credit out loud, pass on useful information instead of keeping it, help a colleague where others can see. One clear signal that cooperation is safe here shifts the room more than any complaint about the culture.	1	204
205	Name what made the collaboration work and say it aloud, A stretch of productive collaboration is not luck, it has a cause: a clear handoff, a quick decision, the right people in one place. Work out which one it was. Then say it to the people involved, plainly: this worked because we did X. Naming the cause out loud is how a good few days becomes a way the group chooses to work again.	1	205
206	Recreate whatever made the focus time possible, Focused time that produces real work usually came from one specific thing: an early start, a closed door, a morning with no meetings. Notice which it was for you. Then put the same condition into next week on purpose, before the calendar fills with everything else. Focus time you schedule survives. Focus time you hope for gets taken.	1	206
207	Write the decisions down before they turn vague, A clear decision today becomes a fuzzy memory within two weeks, and fuzzy decisions get quietly relitigated. Protect the ones you just made. Write them somewhere the group can see: what was decided, who owns it, by when. Three lines now saves the conversation where everyone half-remembers something different and the productive few days unravels.	1	207
208	Ask what decision the meeting needs to reach, Meetings drift when no one has said what they are for, and you can force that quietly. Before your next meeting, message the organiser one question: what decision do we need to walk out with? Either they have an answer and the meeting gets sharper, or they realise there is not one and it gets shorter. Asked before, not during, it never reads as a complaint.	1	208
209	Make someone else rank the priorities you are juggling, When everything is priority, you end up ranking them yourself by accident, usually by whatever shouted last. Take that decision out of the shadows. Write your competing priorities in a short list, take it to whoever owns them, and ask them to put the list in order. They may resist, because ranking is hard, but that difficulty was yours to carry until now.	1	209
210	Send the specific question instead of booking their time, Hard-to-reach people are hard to reach for a meeting, not always for an answer. Next time you need someone, do not ask for a slot. Send the one specific question in writing, with the context they need and the decision you are trying to make. Many will reply in two lines to something they could never find thirty minutes for.	1	210
211	Sharpen the question you reflect on each week, A reflection habit you already keep is worth upgrading rather than just maintaining. Most reflection circles around how did the week go, which is too broad to produce much. Swap it for something sharper this week: what did I do that I should do again, and what cost me time I will not get back. A better question is what turns the habit into real insight.	1	211
212	Bring one real item to the next team reflection, Scheduled team reflection slowly turns into a routine where everyone offers something safe and general, and nothing changes. You can keep yours honest. Before the next one, prepare a single specific item: a real thing that did not work, or a concrete idea worth trying. One person bringing something genuine is usually enough to pull the rest of the session out of autopilot.	1	212
213	Bring the question you most want to think through, A regular reflection conversation with your manager gives you the most when you arrive with the agenda rather than waiting to receive one. Before the next one, decide the single thing you genuinely want to think through: a decision, a pattern, a stuck point. Bringing it makes the time yours and turns a check-in into something that actually moves your thinking.	1	213
214	Shrink reflection to one question at the end of the day, Reflection loses to a full workload because it sounds like a session you have to find time for. Shrink it until it fits time you already have. Before you close your laptop today, answer one question: what is the most useful thing I learned this week? Sixty seconds, no notebook needed. Done daily, that beats the reflection most people manage in a quiet month.	1	214
215	Attach reflection to something already in your routine, Waiting for reflection to become a team priority means waiting indefinitely. You do not need the routine to change, you need to attach to it. Pick something that already happens every day, the first coffee, the commute, the end of a stand-up, and make that your reflection cue: one question, same moment, every day. The trigger already exists, you are just giving it a second job.	1	215
216	Reflect with two fixed prompts instead of a blank page, Reflection feels hard when it is open-ended. A blank page is intimidating and you are never sure you are doing it right. A fixed structure fixes that. At the end of a task or day, answer two things only: what worked and is worth repeating, and what I would change next time. Same two prompts every time. The skill is not insight, it is repetition.	1	216
217	Tell your manager what made a message work, Clear communication quietly fades when nobody reinforces it. Next time a message lands well, reply with one line naming what made it work, like the summary up top or the single clear ask. Managers mostly hear about confusion, so telling them what worked is how you get more of it.	1	217
218	Pass what you know to a teammate who would miss it, Being well informed is an advantage not everyone on your team shares. Pick one update you have that a teammate would otherwise miss and pass it to them today. It costs a minute and builds you into the person others rely on to stay current.	1	218
219	Close your next conversation by saying the plan back, Mutual understanding holds because someone does the checking, so make that someone you. At the end of your next conversation, say the agreed next step back in your own words and ask if you have it right. Ten seconds there catches the small misreads before they cost you a reworked day.	1	219
220	State your read then ask them to confirm it, When a message is unclear, skip the open "can you clarify" question, which often returns the same vagueness. Reply with your best read stated plainly instead: "I'm reading this as X, done by Friday, correct?" Confirming is faster for them than re-explaining, and you get a precise answer plus a record of it.	1	220
221	Stop treating messages as a live feed, The overwhelm here is not the number of messages, it is treating them as a live feed you have to watch. Pick two or three set times today to work through messages in a batch, and close the channels in between. The same load costs far less attention that way.	1	221
222	Keep one note that holds the current truth, When information is scattered and inconsistent, stop holding the current picture in your head. Open one running note today for your main projects and write the latest known state of each. Update it whenever something new or conflicting lands. It becomes your single reliable reference while everything around it stays messy.	1	222
223	Put one thing you learned to real use this week, Most personal development leaks away because it is never applied. Pick one specific thing from your recent learning and find a real task this week to use it on, even a small one. Using it once is what turns it from something you read into something you can actually do.	1	223
224	Tell your manager the goal behind the skill you are building, Managers hand out opportunities partly on guesswork about what people want. Right now your development lines up with where you are headed, but that alignment is not guaranteed to last. Say in one sentence what you are building toward, so the next opportunity that comes up gets aimed at you on purpose.	1	224
225	Write down what you could not do three months ago, Progress motivates you only as long as you can see it, and a new skill stops feeling like an achievement the moment it becomes normal. Spend two minutes listing a few things you could not do three months ago and now can. On the flat days, that list is the proof you are still moving.	1	225
226	Book development a fixed slot instead of hoping for spare time, Development loses to other work because it shows up as a vague should while everything else has a deadline. It will keep losing that fight. Stop trying to find time and book it instead: one twenty-minute slot in your calendar this week, treated like a meeting you would not skip. Small and scheduled beats large and someday.	1	226
227	Find the stretch inside the work you already have, When there is no formal training on offer, it is easy to conclude there is no development at all. But most growth was never a course. It is a harder version of something you already do. Pick one task on your plate this week and deliberately do it a level above your usual. The work itself becomes the training.	1	227
228	Judge each option against a real problem you have now, Development choices look low value when you judge them in the abstract. The same option becomes useful the moment it is tied to a real problem. Run the current list through one sharper question: would this help with the specific thing I am stuck on right now? The ones that survive that question are worth your time.	1	228
229	Tell your manager the alignment is working, Managers keep doing what they get feedback on. The support you are getting to align your work with your goals will quietly fade if it looks unnoticed. In your next check-in, name one specific piece of work that is moving you toward your goal and say plainly that it helped. That keeps the next assignment pointed the same way.	1	229
230	Pick the one opportunity that fits and move on it, Opportunities that match your ambitions only count if you act on them, and they have a way of staying theoretical. Choose the single one that fits best and take one concrete step this week: register interest, ask who owns it, find the entry point. An opportunity you have started is worth more than three you are still admiring.	1	230
231	Write your next milestone down so you can check it, Career and personal goals feel in sync right now, but sync drifts silently as both change. Write your next career milestone in one line, alongside the personal goal it serves. Keep it where you will see it in a few months. The note turns a current good feeling into something you can actually test later, before the drift gets large.	1	231
232	Ask one person what the next step actually looks like, Progression feels limited partly because no one has mapped it for you, and you cannot navigate a path you have not seen. Pick one person who has done the role above yours and ask them directly what the step up actually required. You may find the path exists and was simply never described. Either way you stop guessing.	1	232
233	Bring your manager the goal and the gap rather than a complaint, Nobody aligns your work to your goals unprompted, because your goals live in your head, and waiting reads as contentment. Book fifteen minutes with your manager and bring two things: the specific goal, and the one skill or experience you are missing for it. A clear gap is something a manager can act on. A vague wish is not.	1	233
234	Define the aspiration in one concrete sentence first, A career path cannot connect to an aspiration that is still vague. Before deciding the two are disconnected, write your aspiration as one concrete sentence: not be successful, but something specific enough to test. Then hold your current path against it. Often the disconnect shrinks once the aspiration is defined, and the real gap becomes something you can name.	1	234
235	Use the openness out loud so others see it is real, An open culture stays open only while people keep using it visibly. Quiet agreement signals nothing. This week, say one honest thing in a meeting that you might normally keep to yourself, calmly and constructively. Others watching learn that candour is genuinely safe here, and the culture you are benefiting from gets reinforced rather than assumed.	1	235
236	Share one of your own misses where your team can hear, Leaders modelling openness about failure only works if it travels, and you can carry it. Next time it fits, talk about one of your own recent mistakes and what it taught you, where teammates can hear it. When the example comes from a peer rather than only from leadership, it stops being a leadership behaviour and becomes a normal one.	1	236
237	Do for someone else what was just done for you, Being met well when you gave feedback is what makes you willing to do it again, and the same is true for everyone. Next time a colleague offers you feedback or raises something hard, respond the way you would want to be met: thank them, take it seriously, act visibly. Reinforcement spreads, and you can be a source of it.	1	237
238	Test the water with one low-stakes honest comment, Fear of repercussions makes every honest comment feel like a large bet. Make a small one first. Pick a low-stakes topic and say one true thing about it, framed as an observation rather than a verdict. Watch how it lands. A few small, safe attempts give you real evidence about what is actually risky here, which is more reliable than the fear.	1	238
239	Make your own channel with one direct request, An official feedback channel is convenient, but its absence does not mean you have no route. You can make one. Ask one person who can act on it for ten minutes, and say up front it is to share some honest input. A direct, named conversation is a channel. You do not need a form to be heard, just a willing listener.	1	239
240	Judge the person in front of you rather than the past one, A bad past experience teaches you to be wary of sharing in general, which is broader than what actually happened. The real question is narrower: is this specific person, in this context, safe to be honest with? Judge that one case on its own evidence. Some people will fail the test. Others have been quietly passing it.	1	240
241	Use the easy connection to build one real relationship, Good tools make reaching another team easy, but easy contact and a real working relationship are not the same thing. Use the low friction for something more than a transaction this week. Have one slightly longer conversation with a cross-team contact about how their side works. When you next need them, you are reaching a person you know, not a name in a system.	1	241
242	Pull one person on the edge into the community, A strong sense of community is easy to enjoy from inside and easy to be outside of. Look for one person, a new joiner or someone from another team, who is not quite in it yet. Make one inclusive move this week: an introduction, an invite, a question that brings them in. Communities stay strong by widening, not by closing around who is already in.	1	242
243	Take the encouragement up on one specific thing, Cross-team collaboration being encouraged only matters if someone acts on it, and most people wait for a reason. Make your own. Find one thing another team is doing that overlaps with yours and reach out this week to compare notes. Encouragement that nobody uses quietly stops being real. Using it keeps the door open for everyone, including you next time.	1	243
244	Build one named contact across the silo, Silos hold because work has to cross them through process, and process is slow. A person is faster. Pick the team you most often get stuck waiting on and find one specific person there to know by name. A direct relationship routes around a structural silo. You will not fix the silo, but you will stop being fully blocked by it.	1	244
245	Attach connection to a task you already have, Waiting for an opportunity for meaningful connection usually means waiting, because those opportunities are not scheduled. Attach it to existing work instead. Next time a task touches another team, use it as the reason for a real conversation rather than an email. The work was happening anyway. You are just letting it carry a connection it would not otherwise make.	1	245
246	Make one trip through the channel do the whole job, Strict channels are slow mostly because requests go through them half-formed and bounce back. You cannot remove the channel, but you can make one trip count. Before you send the request, include everything the other team needs to say yes without a follow-up: context, the specific ask, the deadline. One complete pass beats three incomplete ones.	1	246
247	Act on one suggestion within 48 hours, Specific, actionable feedback has a short shelf life. Act on it within two days and it sticks. Leave it a fortnight and it becomes a note you meant to use. Pick the single most useful suggestion and apply it to a real task this week, then tell your manager you did. Acting fast cements the change and makes the next round of feedback sharper.	1	247
248	Write the clarified expectation down and confirm it, Feedback that clarified what is expected is valuable, and verbal clarity fades. Capture it. Write the expectation in your own words in two lines, then send it back to your manager: here is what I took from that, have I got it right? The written version protects you from drift, and the confirmation catches any gap before it costs you work.	1	248
249	Pin down the exact thing that was praised, Positive reinforcement feels good but often stays general, and you cannot repeat a good feeling. Turn it into something concrete. Work out the specific behaviour behind the praise: the choice, the habit, the bit of effort. Name it to yourself in one sentence. Once it is a named action rather than a compliment, it becomes something you can do again on purpose.	1	249
250	Ask a narrow feedback question instead of an open one, Open requests for feedback tend to return nothing useful, because how am I doing is too broad to answer well. Ask something narrow instead. Pick one specific thing, a recent piece of work or a particular skill, and ask your manager one pointed question about just that. A small, specific question is easy to answer honestly and gets you feedback that is actually relevant.	1	250
251	Ask what you should do differently next time, When feedback lands as criticism, the useful part gets buried under how it felt. You can dig it out with one question. Ask: what specifically would you want me to do differently next time? That turns a verdict about the past into an instruction for the future, which is the only part you can use. It also signals you are listening for the substance.	1	251
252	Ask for feedback at a checkpoint rather than the end, Feedback arrives too late when it is only given on finished work. Move the moment yourself. Next time you have something substantial underway, show it to your manager at a halfway checkpoint and ask for input then. Feedback on work in progress can still change the outcome. Feedback on a finished thing can only change the next one.	1	252
253	Give someone else the listening you just got, Being genuinely listened to is noticeable because it is not the norm. You know how it feels, so pass it on. In your next conversation, give one person that same attention: no interrupting, no half-listening while planning your reply, a clear acknowledgement of their point before you add yours. The understanding you value spreads when people copy it, and you can start the copy.	1	253
254	Come to the next discussion with input ready, When your input reliably prompts real discussion, that is an open door, and open doors reward people who walk through them prepared. Before your next meeting, decide the one point you most want to land and bring it ready, not improvised. A valued voice plus a prepared point is how a contribution turns into an actual decision rather than just a good exchange.	1	254
255	Keep the checking habit that builds the understanding, Mutual understanding in conversations is not automatic, it is produced by small habits: summarising, checking, asking. It holds while those habits hold. Keep doing your part deliberately. End your next few conversations by briefly restating what you both agreed. The understanding you are enjoying is maintenance, not luck, and you are one of the people maintaining it.	1	255
256	Lead with the point and pick a better moment, Input meets distraction when it arrives at a bad moment or buries the point. You control both. Next time, open with your conclusion in one sentence rather than building up to it, and pick a moment when the person can actually attend, not the end of a packed meeting. The same input given sharply at the right time gets a very different reception.	1	256
257	Put the point in writing and check it landed, A point made once, out loud, in passing is easy to misread or miss. Give it a better chance. Put your key point in two written lines so there is a record, and after you raise it, ask one question to check how it was understood. Misinterpretation usually survives because nobody checks. A quick check is what catches it.	1	257
258	Do the clarifying yourself by stating your read, When nobody else makes the effort to clear up a misunderstanding, it sits there and costs everyone. You can do the clarifying without waiting. State your understanding plainly: here is what I think we mean, here is what I think we decided. Then ask if that matches. You cannot make others clarify, but you can put a clear version on the table for them to correct.	1	258
259	Tell the person their feedback actually helped, Helpful, timely feedback takes effort to give, and people give more of what they see land. Close the loop. Go back to whoever gave it and tell them specifically what you changed because of it. That does two things: it cements the change for you, and it makes that person far more likely to invest in feedback for you again.	1	259
260	Check whether the new approach actually worked, Feedback that pushed you to try a new approach is only half complete. The other half is finding out if it worked. After you have used the new approach once or twice, take two minutes to judge it honestly: better, worse, mixed? Capture the verdict. That is how a one-off experiment becomes a deliberate part of how you work, or gets dropped on purpose.	1	260
261	Apply the new perspective somewhere it has not been, A new perspective is most valuable once you find how far it stretches. So far it has changed one thing. Deliberately apply it to a second, unrelated situation this week and see if it still helps. A perspective that works in more than one place becomes a real tool. One that only fitted the first case was just good advice for that case.	1	261
262	Turn feedback into a standing request rather than a hope, Patchy feedback stays patchy while it depends on someone remembering to give it. Make it routine on your side. Ask your manager to spend the last five minutes of an existing regular meeting on one piece of feedback. A fixed slot in a meeting that already happens turns feedback from something occasional into something you can count on.	1	262
263	Ask for the fix as well as the fault, Feedback that names the negative without telling you what to do leaves you stuck. Ask the missing question directly: if you were doing this, what would you do differently? That moves the person from describing the fault to describing the fix. It is a fair, easy question to ask, and the answer is the part you can actually act on.	1	263
264	Ask for a quick review before the work is finished, Feedback you get too late cannot change anything except the next job. Move it earlier yourself. On your next significant piece of work, build in a short mid-point review and ask for input while there is still time to use it. You do not need to wait to be offered feedback at the end. You can ask for it at the point it can still help.	1	264
954	Build your own route by saying yes to a cross-team request first. Routes don't exist until someone walks them. Find one piece of work outside your immediate team that needs a hand and offer. The contacts and the visibility you gain start to create the very route the team lacked.	1	1271
265	Name the next visible marker on your growth path, A clear growth path stays motivating while you can see the next marker on it. Decide what yours is: the next skill, the next responsibility, the next conversation. Write it down with a rough timeframe. A path you can name a specific next step on keeps pulling you forward. A path that is only clear in general slowly turns abstract.	1	265
266	Spend the allocated time on one deliberate thing, Time set aside for growing skills is easy to lose to whatever is urgent that day. Protect it by deciding in advance. Before your next allocated slot, pick one specific skill and one specific thing you will do with the time. Allocated time with a plan produces real progress. Allocated time without one quietly gets eaten by the inbox.	1	266
267	Take up one initiative outside your team this quarter, Being allowed to work on initiatives beyond your team is a real growth lever, and permission you never use is the same as no permission. This quarter, find one cross-team initiative that would build something you want and put your hand up for it. The freedom is only worth having if it changes what you actually do.	1	267
268	Bring the agenda that makes the conversation real, A career conversation feels hollow when neither side comes prepared and it stays general. You can fix half of that. Before the next one, bring two concrete things: where you want to be in a year, and the one decision or piece of support you need from your manager now. A conversation with a specific ask in it cannot stay hollow.	1	268
269	Add one new element to the role yourself, Stagnation usually means the role has stopped changing, and you are waiting for it to. You can move first. Pick one new element to add deliberately this month: a skill the role does not require yet, a problem nobody owns, a part of the work you have avoided. You cannot always change the role, but you can change what you are doing inside it.	1	269
270	Frame the move as growth rather than leaving, Talking about opportunities beyond your team feels off-limits because it sounds like one foot out the door. Reframe it before you raise it. Lead with the skill or experience you want to build and why it makes you better at your work, not with the wish to leave. A manager can support growth far more easily than they can support an exit.	1	270
277	Name the strength the method is using, When a way of working fits your strengths, it is worth knowing exactly why. Name the specific strength the method draws on: structure, problem-solving, working with people, whatever it is. Once you can name it, you can look for that same fit deliberately in other tasks, and ask for work that uses it. A known strength is a strength you can aim.	1	277
278	Use the choice instead of defaulting to habit, Autonomy over your methods only pays off if you actually use it, and it is easy to keep doing things the familiar way out of habit. Pick one task this week and genuinely choose its method: would a different approach be faster, better, more satisfying? The freedom to choose is worth nothing on autopilot. It is worth a lot when you stop and use it.	1	278
279	Spend the creative room on one real improvement, Room for creativity in how you work is only valuable when something gets made with it. This week, use it once on purpose: take one task and try a genuinely different or better way of doing it. Slack in a process that nobody uses quietly disappears. Used, it produces the improvements that made the room worth having.	1	279
292	Make the work visible rather than just the result, Effort goes unnoticed when only the finished result is visible and the work behind it is not. You can change that without boasting. When you share an outcome, add one factual line about what it took: the problem solved, the time it ran, the obstacle handled. That is information, not bragging, and it gives people something to recognise. Invisible effort is hard to appreciate.	1	292
293	Set the example with one specific acknowledgement, Generic acknowledgement, nice job, thanks team, spreads because it is the easy default. You can shift it by example. Give one piece of recognition this week that is genuinely specific: name the exact thing, say why it mattered, say it to the person directly. People tend to copy the standard they see. Raising it once, visibly, does more than wishing the standard were higher.	1	293
294	Attribute your own part clearly and early, Contributions get overshadowed when it is unclear who did what, and that gap gets filled by whoever speaks first. Fill it factually. When you share work, be plain about your specific part: here is the piece I owned and what it involved. Stated early and matter-of-factly, that is just accuracy. Left unsaid, your part quietly merges into someone else's.	1	294
295	Take training in small pieces the workload can absorb, When workload blocks training, the problem is usually the size of the block, a full day you will never find. Shrink it. Most learning can be taken in twenty-minute pieces: one module, one chapter, one recorded session between tasks. Training that fits the gaps in a heavy week happens. Training that needs a clear day waits for one that rarely comes.	1	295
296	Book onto one training opportunity before it passes, Good training opportunities being available is only worth something if you take one, and they tend to stay on the someday list. Pick the single most valuable one open to you now and register this week, before the slot or the moment goes. Available training that nobody books quietly stops being offered. Booked, it stays a real part of how the place develops people.	1	296
297	Reuse one idea from that training this week, Training that was valuable a while ago has a problem: value fades if it is not used. Bring it back. Pick one specific idea or technique from it and deliberately apply it to a current task this week. Using it once reactivates it. And if it still holds up, that is a strong signal to ask for the next piece of development in that area.	1	297
298	Ask for one specific thing rather than training in general, A general absence of training is easy for a manager to nod at and forget. A specific request is not. Decide the one concrete thing you want, a named course, a skill, time to shadow someone, and ask for that, with the reason it makes you better at the work. A precise ask tied to performance is hard to leave unanswered.	1	298
299	Say what would be valuable instead of what was not, Training that misses is usually designed without knowing what you actually need. Close that gap. Rather than only noting the training was not useful, tell whoever arranges it the specific skill or problem you do need help with. Feedback that names the gap gives them something to aim at next time. Feedback that only says this missed leaves them aiming blind again.	1	299
300	Plan now how you will use the training after it ends, Being in good training is the best time to protect its value, because most learning leaks away in the weeks after it ends. Decide now, while you are still in it, the one or two things you will actually apply when you are back at normal work, and roughly when. Training you plan to use sticks. Training you simply finish mostly fades.	1	300
301	Turn one discussed opportunity into a concrete step, Discussing several internal opportunities with your manager is a good start, and discussion that never converts becomes just talk. Pick the one that fits best and agree a concrete next step with your manager: who to speak to, what to prepare, by when. An opportunity with a date attached moves. One that stays a topic of conversation usually stays exactly that.	1	301
302	Make the next application strong rather than just submitted, Using the internal portal is the easy part. A submitted application and a strong one are different things. Before your next, do two things: tailor it to what that specific role actually needs, and ask one person who knows the area what they would emphasise. A considered application stands out in an internal pool. A quick one mostly makes up the numbers.	1	302
303	Help one colleague follow the path you used, Having moved internally yourself, you hold something genuinely useful: you know how the process actually works. Pass it on. Find one colleague who wants to move and tell them honestly what helped, what to expect, what you would do differently. It costs little, it strengthens the internal mobility culture you benefited from, and it keeps you connected to people across the organisation.	1	303
304	Frame the move as growth your manager can back, Applying internally feels like betrayal mostly in your own head, where it reads as rejecting your manager. Most managers do not see it that way, and developing people who move on reflects well on them. Raise it as growth rather than departure: here is what I want to build toward. Your manager may turn out to be an ally, but only if you let them in.	1	304
305	Find opportunities through people rather than postings, When there is no proper channel for internal roles, the openings still exist, they just travel by word of mouth instead of by posting. So use people. Tell two or three contacts in areas that interest you that you are open to moves, and ask them to flag anything. Most internal opportunities are filled through who knows you are looking, not through a portal.	1	305
306	Ask for the specific reason behind the last no, Being passed over for external hires more than once is discouraging, and it usually points at one specific gap rather than a closed door. Find out which. Go back to a recent decision-maker and ask plainly what the external candidate had that you did not. The honest answer gives you one concrete thing to work on. Without it, you are guessing.	1	306
307	Notice how the good listeners actually do it, Feeling genuinely understood usually traces back to one or two people who listen well. Study them. Notice what they actually do: they ask before they answer, they reflect your point back, they let a pause sit. Those are copyable techniques, not personality. Picking up one of them makes you better at making others feel understood, which is how the experience spreads.	1	307
308	Make yourself easy to understand in the first place, Management working to make sure people are understood is support worth meeting halfway. Help them by being clear at the source. Before raising something important, get your own point sharp: one sentence on what you mean, one on what you need. Clarity from you means less interpreting for them, and the understanding you value gets easier for everyone to maintain.	1	308
309	Keep doing the small thing that lands well, Mutual understanding in most conversations is produced, not given, and you are doing something that helps. Work out what it is: maybe you check before assuming, maybe you ask good questions, maybe you summarise. Name your own contribution and keep doing it deliberately. The conversations that work are not luck, and knowing your part means you can protect it.	1	309
310	Tie your input to what the listener already cares about, Input meets disinterest when it is framed around what matters to you, not to the listener. Reframe before you speak. Connect your point to something the other person is already trying to achieve or worried about: here is how this affects your deadline, your goal, your risk. The same input, attached to their concern rather than yours, suddenly has their attention.	1	310
311	Open with a one-line headline of your point, Points get misinterpreted or missed when the listener has to assemble them from a longer explanation and assembles them wrong. Hand them the finished version first. Open with a single headline sentence, this is the one thing I want you to take from this, then explain. When the conclusion comes first, the detail clarifies it instead of competing with it.	1	311
312	Ask the other person to say back what they heard, When nobody bothers to clear up misunderstandings, they harden. Catch them at the source. After explaining something that matters, ask one light question: so I know I explained it well, what is your take on it? Their answer shows instantly whether it landed. It feels natural, it puts the check on you, and it surfaces the gap while it is still small.	1	312
313	Learn one feature of a tool you do not use yet, Good tools usually do far more than any one person uses. There is value sitting unopened. Pick one tool you rely on and spend fifteen minutes finding a feature you have never used: a shortcut, an automation, a view. Most people use a fraction of what their tools offer. A small look closes a bit of that gap and compounds every day after.	1	313
314	Show one colleague the shortcut that speeds you up, If a tool genuinely speeds up your work, the same trick will speed up other people's, and most of them do not know it. Spend five minutes showing one colleague the specific feature or shortcut that helps you most. Your individual gain becomes a team gain, and the colleague will usually return the favour with something you did not know.	1	314
327	Use a feedback channel for something that matters, Accessible feedback channels are only worth having if real feedback goes through them. Use one this week for something that actually matters to you, not a token comment. Channels that carry genuine input stay open and get taken seriously. Channels that only ever carry safe, minor feedback quietly stop being checked. What you put through it keeps it alive.	1	327
328	Make it cheap for a busy person to hear you, When everyone is too busy to listen well, long or rambling input gets half-heard. Make hearing you cheap. Lead with your point in one sentence, keep it short, and pick a moment when the person is not mid-task. You are not competing for more of their attention, you are asking for less of it by being clearer. Busy people can hear a sharp point fast.	1	328
329	Create a channel by asking one person directly, No formal feedback channel does not mean no route, it means you have to make one. Pick one person who can actually act on what you want to say and ask them for ten minutes, naming the purpose: I would like to share some honest feedback. A direct, scheduled conversation is a channel. You need a willing listener and a clear ask, not a form.	1	329
315	Name what makes the setup work so you can defend it, A technology setup that just works is easy to take for granted until something changes it. Know why it works. Notice the specific things that make it smooth: the integration, the reliability, the tool that does its job quietly. When tooling decisions come up, you can then argue to keep what works, with reasons. People defend what they can describe.	1	315
316	Report the fault with steps and how often it happens, A tool problem described vaguely, it keeps breaking, is easy for support or IT to deprioritise. Make it actionable. Write down exactly what you do, what goes wrong, and how often it costs you time. A precise, repeatable report is something someone can fix and justify fixing. A general complaint joins a long list and waits.	1	316
317	Teach yourself the one part you use most, Waiting for formal tool training can mean waiting a long time, and you do not need a course to get most of the way. Pick the single function you use most often and spend twenty minutes learning it properly from a help guide or a short video. Targeted self-teaching on your highest-use feature closes most of the efficiency gap that training was meant to.	1	317
318	Build one reliable bridge for the worst handoff, When tools do not talk to each other, the time is lost at the handoff: re-keying, copying, checking. You cannot fix the integration, but you can make the worst handoff reliable. Build one small bridge for it, a checklist or a template that makes the manual step fast and error-free. It does not solve the gap, but it stops the gap costing you twice.	1	318
319	Share one success with the why behind it, Being encouraged to share successes is only worth something if the sharing happens, and many people quietly skip it. Take it up this week. Share one real win, and include the useful part: not just what happened, but what you did that made it happen. A success shared with its cause teaches something. Shared alone, it is just an announcement.	1	319
320	Amplify someone else's win the way you would your own, If sharing your wins lifts the mood, the same is true when you lift someone else's. Do that this week. When a colleague does something good, say so where others can hear, and be specific about it. You already know the positive effect because you have created it yourself. Pointing it at other people's successes spreads it wider than your own ever could.	1	320
321	Share the lesson inside the success rather than just the news, People appreciating shared successes is a good sign, and you can make those moments do more than feel nice. Next time you share a win, add the transferable part: the one thing you learned or did that someone else could use. That turns a pleasant update into something genuinely useful, and it quietly positions you as someone worth listening to.	1	321
322	Mark one win yourself instead of waiting for the culture, A culture that does not celebrate wins will not start celebrating on its own. Someone goes first. Mark one win this week yourself: acknowledge a colleague's good work out loud, or close your own task by naming what went well before rushing to the next. Celebration is a behaviour before it is a culture, and behaviours spread from whoever models them.	1	322
323	Share the work and the credit rather than the glory, Sharing a win feels like gloating when it sounds like look what I did. Change what the sharing is about. Frame it as information, here is what worked and might help, and as shared credit, name everyone who contributed. Done that way it is generous, not boastful. The discomfort is in the framing, not in letting good work be seen.	1	323
324	Connect the win to a goal people are tracking, A success goes unnoticed when it floats free of anything people are paying attention to. Anchor it. When you share a result, tie it to a goal or metric the team or your manager already cares about: this moved that number, this unblocked that priority. Connected to something tracked, the win registers. Left unconnected, even a real achievement slides past.	1	324
325	Bring real input to the check-ins your manager offers, A manager who regularly asks for your input is offering something many people do not get. Keep it worth their while. Come to the next check-in with one genuine thing: a real observation, a concern, an idea, not just fine, all good. A check-in that consistently produces something useful stays on the calendar. One that produces nothing slowly becomes a formality.	1	325
326	Use the open dialogue to bring in a quieter voice, An inclusive culture stays inclusive when people actively use it that way, not just enjoy it. Next time you are in a discussion, notice who has not spoken and make room: ask their view directly, or build on a point they made earlier. Open dialogue widens when the people already comfortable in it pull others in. You can be one of those people this week.	1	326
721	Use the early notice to actually get ahead. Getting changes early only matters if you use the lead time and it is easy to file the news and carry on as before. When you next get an early heads-up take fifteen minutes to do one concrete thing the change will require before it lands. Early notice is worth the most to the person who treats it as a head start rather than just a notification.	1	961
722	Show your manager the briefings are working. A manager who reliably briefs you when plans shift is rare and the habit holds while they can see it is worth their effort. Make that visible. After the next early brief send one line back saying how you used it: this gave me time to do X. A small acknowledgement keeps the rhythm strong rather than letting it slowly slip back to silence.	1	962
723	Set a fixed time to scan the channel each day. An early-posting channel is only useful when you check it before the change starts to bite and ad-hoc checking misses things. Pick one fixed point in your day the start just before lunch end of day and make scanning the channel part of it. A reliable channel paired with a reliable habit is what turns it from a noticeboard into actual early warning.	1	963
724	Watch the upstream signals before changes land. When leadership does not give early warning the signs of a change usually exist before the announcement: senior meetings clustering a project paused a budget review. Notice those patterns and ask one direct question of someone in the know: is anything changing in this area? You will not always get a straight answer and you will often get more notice than the official channel ever gives.	1	965
725	Build one sideways contact closer to the source. When your manager hears about changes when you do you cannot rely on them as your early warning. Build a sideways route. Find one person in another team or function who tends to hear things earlier and put a bit of relationship effort in. A single well-placed contact asked occasionally what is brewing gets you context your formal line manager genuinely cannot.	1	966
726	Keep one running note of every change rumour. Without a reliable channel change information arrives scattered half-heard in conversations and most of it is gone the next day. Capture it. Keep one running note where you jot anything you hear about a change however small with the date. When something starts to feel off you can scan back and spot a pattern which is more than the missing channel was ever going to give you.	1	967
733	Repeat the link in your own team conversations. Leaders linking work to the mission keeps the purpose alive in big moments and that link fades in the day to day unless someone keeps repeating it. Be one of the repeaters. In your next team conversation say out loud how a current task connects to what leaders have laid out. The connection survives when more than just the top of the org is making it.	1	977
734	Feed your manager what they need to keep showing you impact. A manager who shows you the impact of your work needs information to do it and they will do it more easily when they have it. After a piece of work ask what they saw on the other side or share what you noticed yourself. The exchange keeps them invested in tracking impact for you rather than only doing it when something stands out.	1	978
735	Hear it from the person on the other side. Seeing how your work helps people is more sustaining when you hear it in their words rather than just observe it from a distance. Ask one person directly a customer or colleague who used what you made what it actually changed for them. A specific account from the person on the receiving end carries more weight than any general sense that the work helped and it is easy to ask for.	1	979
736	Draw the line yourself between work and a goal. When nobody connects your work to a bigger goal you can connect it yourself. Pick a goal you care about the team's the company's or your own and write one sentence linking your current work to it. The line does not need anyone to authorise it. It changes how the same work feels and gives you something concrete to say next time someone asks why this matters.	1	981
737	Ask the why question directly when work lands. Managers often skip explaining why work matters because they assume you already know and you cannot tell unless someone says it. Make it easy to find out. When a piece of work is handed to you ask one direct question: what is this feeding into and why does it matter? A short fair question gets you the context the briefing left out and gradually trains your manager to include it.	1	982
738	Follow the thread one step downstream yourself. When you never see what happens after your work leaves you the outcome usually still exists just past where your view stops. Trace one piece. Pick a recent thing you made or handed off find who received it and ask what they did with it and what changed. You may need to ask twice. The outcome was always there. You just have not followed it past the handoff.	1	983
745	Protect whatever gave you the focus time. Focus time that produced real work usually came from one specific thing: an early start a closed door a deferred meeting a defended block. Notice which it was. Then put that same condition into next week deliberately before the calendar fills. Focus time you actively protect survives. Focus time you assume will be there gets taken by something louder before you reach it.	1	993
746	Notice what made the priorities clear and reuse it. Knowing what to tackle because priorities were clear is the system working well and clarity is not guaranteed on the next stretch. Notice what produced it: a conversation with your manager a written list a Monday plan. Then ask for or do the same thing deliberately next week. Clarity you can trace to a cause is clarity you can reproduce rather than hope for.	1	994
747	Frame the next ask so it is quick to decide. Quick decisions come back fast because the question was easy to answer rather than just because the decider was responsive. Keep doing that. When you next need a decision present the options your recommendation and what you need then ask for a yes or no. A decision shaped for speed moves fast. An open question framed as a problem stalls even with the most responsive person.	1	995
748	Audit one recurring meeting and drop it. Back-to-back meetings stack because each one looks individually reasonable and nobody adds them up. Audit one. Pick the recurring meeting you get least from and ask the organiser honestly whether you are needed every time or could read the notes. Worst case nothing changes. Often they have wondered the same thing and you have bought back an hour every week.	1	997
749	Force one ranking conversation this week. Starting and stopping happens when priorities have never been ranked out loud and the choice keeps happening in your head all day. Take that decision somewhere it belongs. List what is competing take it to whoever owns the work and ask: if only one can move this week which is it? A single named top priority does not clear the list but it stops the constant context-switching.	1	998
750	Make the block easy to unblock and switch work. Being blocked waiting on a decision usually costs more than the decision itself because waiting feels like working. Two moves help. First make the unblock easier: present the choice clearly with a recommended answer. Second switch to other work the moment the request is sent and check back when a reply lands. Waiting actively is the most expensive way to work.	1	999
757	Check your week's work against the goals on Friday. Written visible team goals only do their job if work is actually checked against them and that habit slips quickly. On Friday take five minutes to look at the goals and ask one question: did this week's work move any of them forward? If most of it did not that is information. The written goal is only useful while someone is reading it and someone is you.	1	1009
758	Act visibly on a restated priority within the week. Managers who keep restating priorities do it partly to check the message is landing and it lands more obviously when you act on it. Within a week of the next restatement do something visible against the priority and tell your manager you have. That confirms the message reached you and makes the restating worthwhile which keeps the habit going rather than fading.	1	1010
759	State back the bit of the alignment that lands on you. Team alignment in meetings only translates to action when individuals leave clear about their own piece. Close that gap yourself. Before the next alignment meeting ends briefly say what you have taken from it for your own work: my piece of this is X. It costs ten seconds surfaces any misread before you walk out and turns shared alignment into one specific commitment.	1	1011
760	Draft your own version and bring it to your manager. Asking for team goals to be set out usually goes nowhere because nobody knows where to start. Start it. Draft what you think the team's three or four key things are right now in your own words then take it to your manager and ask whether that is right. It is far easier to discuss something concrete than to ask for clarity in the abstract and you may get it agreed by tomorrow.	1	1013
727	Tell your manager what each stretch is building. Managers who give stretching work keep doing it when they see it is paying off and that signal is easy to lose under busy work. After a challenging task send one line back naming what the stretch developed: this taught me to handle X. Naming the build keeps your manager calibrated and makes the next assignment more likely to extend you rather than slip back to safe ground.	1	969
728	Name the slightly-beyond task you are working on now. Taking on work slightly beyond your level only builds you while you stay in that zone deliberately and it is easy to drift back once you have proved the point. Name the specific slightly-beyond task you are working on right now. Once you have named it you can finish it and pick the next one on purpose rather than coasting until growth happens by accident again.	1	970
729	Suggest the next reshape before someone has to guess. A role that gets reshaped to keep you growing is the rare case and it stays that way when the reshaping has good input. Supply it. Before your next review name one specific way the role should change next to keep stretching you: a responsibility to absorb a skill to apply a problem to take on. Suggesting the next direction makes it likely the role moves in one you actually want.	1	971
730	Raise the bar on an existing task this week. When tasks have stayed the same for too long waiting for new ones can mean waiting indefinitely. The variable you control is how you do the existing ones. This week pick one task you have run on autopilot and deliberately do it at a higher level: tighter faster and cleaner or simply done in a new way. The task did not change. You changed what happened inside it which is where the growth lives.	1	973
731	Ask for one specific piece of harder work. Managers tend to hand new work to people who have asked for it rather than to people who quietly hope for it. Move yourself onto the list. Tell your manager in plain terms the specific kind of harder work you want next and why you are ready for it. A precise request is easy to act on. A general wish to be stretched is easy to nod at and forget by the end of the day.	1	974
732	Volunteer for what nobody else is putting up their hand for. When there is no formal mechanism for harder work the route is volunteering for what others have not. Put your hand up this month for one thing: the project with no clear owner the problem that has been sitting unsolved the work that looks uncomfortable. Most growth opportunities are not advertised. They are claimed by whoever was first to say I will take that on.	1	975
739	Be the one who jumps in this week. A team norm of jumping in only holds while everyone supplies it rather than just receives it. Make sure you are giving as readily as you are getting. This week watch for one colleague who is visibly under it and step in unprompted: take a task offer cover look something over. The culture you are benefiting from is built from individual acts. Add one this week.	1	985
740	Tell your manager pressure is building before it peaks. A manager who steps in when you are under pressure can only act on what they know about. If you wait until you are drowning the support arrives late. Flag pressure earlier and specifically: here is what is stacking up here is where I will need help. Early notice lets a supportive manager act when it changes the outcome rather than just rescue you at the end.	1	986
741	Name it out loud when a colleague covers for you. Colleagues offering cover before being asked is a habit that holds because people notice it pays back. Keep it noticeable. Next time a colleague covers for you say so out loud and specifically where others can hear: thank you for picking that up it let me handle X. Visible acknowledgement makes the behaviour more likely to repeat and quietly invites others to model it.	1	987
742	Build one mutual-aid pair if the team will not. When your team leaves people to sink or swim the answer is not the team. It is one person. Find a colleague you can build a mutual-aid pair with that is explicit two-way and low-key. We will check in on each other and help when needed. One reliable backup person gives you most of what a supportive team would and you have control over building it without changing the wider culture.	1	989
743	Build a support route that does not depend on your manager. If your manager is absent when pressure builds waiting for that to change leaves you exposed. Build a route that does not need them. Identify one or two people you could go to under pressure such as a peer a senior colleague or a cross-team contact and warm those relationships now. Support arranged before you need it works. Support sought for the first time during a crisis usually does not.	1	990
744	Reframe the ask as a trade rather than as needing help. Where asking for help is frowned upon the issue is often how the ask sounds. Reframe it. Instead of saying I need help frame it as a quick check or a sense-check: can I run something past you or can you eyeball this. The same request framed as collaboration rather than dependence is easier for both sides and less likely to draw the disapproval that comes with being seen to struggle.	1	991
751	Sharpen the question your reflection slot asks. A regular reflection slot is worth keeping and it is worth upgrading too. Most reflection asks how did the week go which is too broad. Use a sharper question this week: what did I do that worked and should repeat and what cost me more time than it should have. A better question is what turns a steady habit into one that actually changes how you work.	1	1001
752	Bring the question you most want to think through. Reflection built into your 1:1s gives you the most when you arrive with an agenda rather than waiting to receive one. Before the next one decide the single thing you most want to think through: a decision a pattern a stuck point. Bringing it makes the slot yours and turns a check-in into something that moves your thinking rather than a status update with reflection bolted on.	1	1002
753	Bring one real item to the next team retro. Team retros slowly turn into ritual where everyone offers something safe and nothing changes. Keep yours honest. Before the next one prepare a single specific item: a real thing that did not work or a concrete change worth trying. One person bringing something genuine is usually enough to pull the rest of the session out of autopilot.	1	1003
271	Share one recent lesson from a mistake at your next team meeting.	1	271
272	Thank a colleague who handled a mistake with grace and tell them it mattered.	1	272
273	Follow their lead by sharing one thing that did not go as planned this week.	1	273
274	Write down one mistake privately and identify what you would change next time.	1	274
275	Find one trusted colleague and share a small lesson learned over coffee this week.	1	275
276	Start a private 'lessons learned' note for yourself after each project wraps up.	1	276
330	Step out of your lane once with one small comment, Staying in your lane is partly a decision you have made, which means you can revise it. You do not have to become the loudest voice in the room. Pick one moment this week where you have a real view and say it, briefly. A single well-placed comment is low-risk, and it tells people you have something to add. Being heard starts with speaking once.	1	330
331	Plan where this learning will be used, Being engaged in something new is good, and learning only pays off when it meets real work. Decide now, while you are still in it, one specific place you will apply what you are picking up, and roughly when. A learning experience with a planned use turns into a skill. One without a use stays an interesting thing you once did.	1	331
332	Pass on the resource that helped you most, Good learning resources are more valuable when more people know they exist. Tell one colleague about the single resource that helped you most, and why. It costs a sentence, it helps them, and it tends to come back: people who share resources usually get told about others in return. Accessible resources only help the people who actually know about them.	1	332
333	Book the learning time the permission gives you, Leadership encouraging time for learning is permission, and permission unused is the same as none. Convert it. Put one specific learning block in your calendar this week and treat it as real. Encouragement that nobody acts on quietly fades, and so does the time it was meant to protect. Booking it keeps the permission real for you and visible to others.	1	333
334	Manufacture an opportunity by asking to be involved, When no learning opportunities are visible, it usually means none are packaged as opportunities, not that none exist. Make one. Find a project or a problem near you that involves something you do not yet know, and ask to be included, even in a small way. Real learning is more often a door you ask to walk through than a course someone hands you.	1	334
335	Pick the task that makes you learn something, When workload leaves no room to explore learning, stop treating learning as separate from the work. Look at what you already have to do and choose to take on, or volunteer for, the one task that will force you to learn something new. The workload stays the same. You have just selected the part of it that also develops you.	1	335
336	Search around your real interest rather than the menu, When the learning on offer does not match your interests, you are letting someone else's menu set the options. Set your own. Name the specific thing you actually want to get better at, then go looking for a way to learn it: a person to ask, an article, a small project. The relevant opportunity usually exists. It is just not on the list you were handed.	1	336
337	Run one real experiment this week, A culture that supports experimenting only stays that way while experiments actually happen. Be one of the people who supplies them. Pick one small thing this week and try it a genuinely different way, accepting it might not work. A culture of experimentation is just a stream of individual experiments. If everyone waits for someone else to go first, the support quietly goes unused.	1	337
338	Take one considered risk while the support holds, Management backing reasonable risk, even when it fails, is rarer than it should be, and it is the kind of support that can quietly change. Use it while it is real. Take one considered risk this month: propose the bolder option, try the unproven approach. Permission to fail is only valuable to people who actually use it to attempt something.	1	338
339	Use the momentum to try the next new thing, Positive reinforcement for trying something new is fuel, and fuel works best when you use it soon. Do not let it settle. While the encouragement is fresh, pick the next slightly-uncomfortable thing and attempt it. Confidence built on one success grows fastest when the next attempt follows quickly. Leave too long a gap and the comfort zone closes back over.	1	339
340	Take one small risk where the downside is contained, Waiting for explicit encouragement to step outside your comfort zone can mean waiting forever. You can start without it. Pick one risk small enough that the downside is contained: a new approach on a low-stakes task, speaking up in one meeting. You do not need permission for something whose worst case is manageable, and a few of those build the appetite for bigger ones.	1	340
341	Do an existing task in an uncomfortable new way, Being too busy blocks stepping outside your comfort zone only if you think it needs a separate project. It does not. Take a task already on your list and do it in a way that stretches you: present it instead of emailing it, lead the part you usually leave to others. Same task, same time, but done in the uncomfortable direction that makes it growth.	1	341
342	Shrink the risk until it reads as a small test, A risk-averse environment rejects things that look like big bets. So do not present one. Take the new idea and shrink it into a small, time-boxed test: let us try this with one client for two weeks and review. Framed as a contained experiment with a clear off-ramp, the same idea becomes something a cautious environment can say yes to.	1	342
343	Follow up so the conversation becomes a relationship, A conversation that went beyond work is a start, and starts fade without a follow-up. Do one small thing this week to build on it: refer back to what they told you, check in on the thing they mentioned, continue the thread. Connection deepens through the second and third conversation, not the first. The follow-up is what turns a good moment into a real relationship.	1	343
344	Have that kind of conversation with someone newer to you, An insightful conversation with a colleague shows you are capable of real dialogue, not just task talk. Point that capability somewhere it is needed more. Pick someone you know less well and make space for one genuine, unhurried conversation with them. You already have the skill. Using it with people on the edge of your network is how it widens rather than just deepening.	1	344
345	Look for common ground with one more person, Finding common ground deepened one relationship, which means you know how to do it. Repeat it deliberately. Pick one more colleague and, in your next conversation, listen for the thing you share: an interest, an experience, a way of seeing the work. Common ground is rarely obvious and almost always there. Looking for it on purpose is what turns contacts into relationships.	1	345
346	Ask one non-task question in your next conversation, Interactions stay superficial when every exchange is strictly about the task. Someone has to break that pattern. In your next conversation, ask one genuine question that is not about work, and listen to the answer. It takes thirty seconds, and it changes the register of the relationship. Depth starts with one question that signals you see a person, not a task.	1	346
347	Turn one routine interaction into a proper conversation, Opportunities for deeper one-on-one time are limited mostly because nobody schedules them. Make one. Take an interaction that would normally be a quick message and turn it into a short call or a coffee instead. Or simply ask one colleague for fifteen minutes to talk properly. The opportunity does not have to arrive. You can convert an ordinary contact into a real one.	1	347
348	Attach connection to something already on the calendar, When time and social barriers block connection, the answer is not finding extra time, it is lowering the cost. Attach the connection to something that already happens: walk to the meeting together, stay two minutes after the call, share the coffee run. Brief, low-pressure moments alongside existing routine add up. Meaningful connection does not need a dedicated slot, just a little intent.	1	348
349	Make one decision you would normally escalate, Being empowered to decide independently is only real autonomy if you use it, and habit makes people escalate things they could simply handle. This week, take one decision you would normally pass upward and make it yourself, with your reasoning ready if asked. Autonomy strengthens when it is exercised. Decisions you keep handing up quietly hand the autonomy back.	1	349
350	Spend the freedom on improving one approach, Freedom to choose how you work is worth the most when you use it to get better, not just to stay comfortable. Take one task this week and rethink the approach: is there a faster, cleaner, more effective way you have not tried because the old way was fine? Freedom used to improve compounds. Freedom used only to repeat the familiar is just habit with permission.	1	350
351	Keep the trust by updating before you are asked, Being trusted to work autonomously is valuable and quietly conditional: it lasts while managers feel informed. Protect it. Send a brief, unprompted update on your main work now and then, before anyone asks. A short proactive update costs little and keeps confidence high. Silence, even when the work is fine, is what slowly invites the checking-in that erodes autonomy.	1	351
352	Remove the uncertainty that drives the micromanaging, Micromanagement is often a manager managing their own uncertainty, not a verdict on you. You can lower the uncertainty. Get ahead of the checking: send a short proactive update on progress and next steps before they ask. When a manager can already see the work is on track, the reason to hover weakens. You are not giving in, you are removing the trigger.	1	352
353	Use the space the policy does not actually cover, Strict policies feel total but rarely regulate everything. Find the space they leave: the sequence, the preparation, the parts genuinely at your discretion, and work that space deliberately. And if one specific policy clearly costs real time, document a concrete example. A policy challenged with evidence of its cost can sometimes move. A general objection to red tape never does.	1	353
354	Build trust with a visible track record on small things, Trust from a manager is rarely granted because you ask for it. It is built from evidence. Pick a few small commitments and deliver them visibly and exactly: on time, as agreed, with a clear update. A short run of done what I said, when I said changes how a manager sees you. Trust follows a track record, and you can start building that record this week.	1	354
355	Turn one piece of self-awareness into a change, A regular self-awareness habit is valuable, and it is only finished when an insight becomes a change. Take the most recent thing you have noticed about yourself, a strength to use more, a habit to drop, and convert it into one concrete action this week. Reflection that consistently produces a change is development. Reflection that only produces awareness is a diary.	1	355
356	Double down on the strength and pick one weakness to work, Feedback that surfaced your strengths and weaknesses gives you two different jobs. For the strength, find one way to use it more this week, since strengths return the most when leaned into. For the weakness, choose just one to work on, not all of them. Trying to fix everything at once usually fixes nothing. One of each is enough to make the feedback count.	1	356
357	Pick the learning goal that solves a current problem, Identifying your next learning goal is easier when you stop choosing in the abstract. Look at what you are actually struggling with or about to need in the next few months, and let the goal come from that. A learning goal tied to a real, near-term problem keeps its motivation, because progress is immediately useful. A goal chosen purely in principle tends to drift.	1	357
358	Spend two minutes a week on one strength and one fix, Reflection on your strengths and weak spots loses to a busy week because it sounds like a session. Shrink it to two minutes. Once a week, answer two things only: what is one strength I used well, and what is one thing I would do better. That is enough to keep developing deliberately. The barrier is the imagined size of it, not the time it takes.	1	358
359	Read your strengths from evidence rather than introspection, Identifying your own strengths and weaknesses by introspection is genuinely hard, because you are too close. Use evidence instead. Notice three things: what colleagues come to you for, what work you finish quickly and well, and what consistently drains you or gets put off. Those patterns point at your strengths and weak spots more reliably than staring inward ever will.	1	359
360	Reflect anyway because development is not only formal, Skipping reflection because no development opportunities exist assumes development only means courses and programmes. Most of it does not. Knowing your strengths and gaps changes how you do your everyday work: which tasks you volunteer for, how you approach them, what you ask for. The reflection is worth doing even with no formal opportunity, because the daily work is itself the development.	1	360
361	Use the early notice to actually get ahead, Being told about changes early only helps if you use the head start, and it is easy to file the information and carry on as before. When you next get early notice of something, take fifteen minutes to do one concrete thing the change will require, before it lands. Early information is worth the most to the person who acts on it early.	1	361
362	Pass the reasoning on to someone who missed it, Understanding why a decision was made is an advantage, and not everyone hears the reasoning. When you next work with someone who seems unclear about a change, take a moment to explain the why behind it, not just the what. People adapt far better to decisions they understand, and you can extend that understanding past where the original explanation reached.	1	362
363	Turn the latest update into one adjusted priority, Updates only keep you aligned if you let them change something. After the next significant update, do not just absorb it. Ask one question: given this, what on my list should move up, down, or off? An update that reorders your actual priorities is doing its job. One that you simply read and nod at has changed nothing.	1	363
280	Find the discretion the imposed method still leaves, An imposed method removes some choices, rarely all of them. Look for the discretion it leaves: the order you do steps, the tools within it, how you prepare. Use that zone deliberately. And if the method genuinely costs time, note one specific example. A concrete cost is something you can take to whoever owns the process, where a general complaint goes nowhere.	1	280
281	Measure the one step that wastes the most time, Inefficiency feels like a general frustration, which is hard to act on. Make it specific. Pick the single step in your work that wastes the most time and roughly measure it: how long, how often. A bottleneck with a number attached, this costs two hours a week, is something you can raise and that someone can justify fixing. Frustration alone is not.	1	281
282	Adapt the edges of the method to how you work, A fixed method usually still has edges you can shape to your style: when you do focused parts, how you prepare, the order within your control. Adapt those deliberately. If one part genuinely clashes with how you work best, ask for one specific accommodation rather than pushing back on the whole method. A single concrete request is far more likely to land than a general objection.	1	282
283	Protect whatever gave you the focus time, Completing something valuable in protected focus time is the workload working well. Notice what made the time exist: a clear morning, a closed door, a deferred meeting. Then put that same condition into next week deliberately, before the calendar fills. A healthy workload is partly luck and partly the conditions you defend. Defend the one that just worked.	1	283
284	Bring your real priorities into the process, A good prioritisation process only works if people put honest information into it. Use yours fully. Next time priorities are set, bring your actual picture: what is genuinely urgent, what is quietly slipping, what is taking longer than expected. The process can only rank what it can see. Feeding it the real state is how it keeps your workload healthy.	1	284
285	Treat the number that works as your limit, Balancing several tasks without being swamped means you have found a number that works for you. Make it explicit. Notice roughly how many active tasks that is, and treat it as a limit, not a coincidence. Next time work threatens to push past it, that known number gives you a concrete reason to push back or renegotiate before the balance tips.	1	285
286	Get one priority named as the genuine top, Too many competing priorities means the ranking is happening by accident, in your head, under pressure. Make it a real decision. Take your list to whoever sets the work and ask one question: if only one of these can move this week, which is it? Forcing a single named top priority does not clear the list, but it tells you where to actually start.	1	286
287	Quote the time the task really takes with evidence, Work overruns when estimates assume an ideal run with no interruptions. Break the pattern with data. For a recurring task, note once how long it genuinely takes start to finish, including the usual disruptions. Then quote that number next time, not the hopeful one. A realistic estimate backed by a real example is hard to argue down, and it stops you absorbing the gap.	1	287
288	Show the full load before you accept the next thing, Constant overcommitment happens because each new task is agreed on its own, with nobody looking at the whole. Make the total visible. Next time you are asked to take something on, do not just say yes. Show what is already on your plate and ask what should move to make room. The trade-off was always there. You are just making it a decision someone has to see.	1	288
289	Pass the recognition on to someone today, Recognition that reaches you is a good moment to widen, because acknowledgement is one of the easiest things to give and one of the rarest to receive. Today, recognise one other person specifically: name what they did and why it mattered. The culture where effort gets noticed is built one comment at a time, and you can add one while it is on your mind.	1	289
290	Notice what made it meaningful and copy it, Acknowledgement that felt personal and meaningful is different from a generic well done, and the difference is learnable. Work out what made it land: it was specific, it named a real effort, it was timely. Then use those same qualities next time you recognise a colleague. You know what good recognition feels like, so you are well placed to give it.	1	290
291	Record the line from your work to the outcome, Your work leading to a visible outcome is exactly the kind of thing that gets forgotten by the time you need it. Capture it now. Write one line linking what you did to what resulted. Kept somewhere you can find it, that note becomes evidence for a review, a case for a next step, or simply a record of impact that memory alone will not hold.	1	291
364	Ask the context question instead of waiting for it, When decisions arrive late and thin, the missing context rarely comes to you on its own. Go and get it. When a change lands without enough background, ask one direct question of whoever announced it: what is driving this, and what does it mean for my work? A short question gets you the context the announcement left out, and it gets it now rather than never.	1	364
754	Shrink reflection to one question at day's end. Reflection loses to a packed week because it sounds like a session you have to find time for. Shrink it until it fits the time you already have. Before you close your laptop today answer one question: what is the most useful thing I learned this week? Sixty seconds no notebook needed. Done daily that beats the reflection most people manage in a quiet month.	1	1005
755	Carve five minutes at the end of your next 1:1. If reflection never comes up in your 1:1s you can put it there yourself without changing the meeting. At the start of the next one ask for the last five minutes to step back: what is one thing worth doing differently next time? It does not need permission or a new structure. Five minutes carved deliberately turns a status meeting into something that actually develops you.	1	1006
756	Run your own reflection and pull one person in. Waiting for a team reflection routine usually means waiting indefinitely. Build your own and widen it quietly. Spend ten minutes once a week on what worked and what to change then share the most useful insight with one trusted colleague and ask what they would add. A two-person habit is easier to start than a team one and it often grows into one without being announced.	1	1007
763	Give the ring-fenced time one specific purpose. Ring-fenced learning time pays back the most when it has a defined target rather than when it becomes browsing time. Before each slot decide the single specific thing you will focus on: one skill one chapter one technique to practice. Time you defended deserves a target. Twenty minutes pointed at something specific produces more than an hour spent drifting around what looks interesting.	1	1017
761	Ask the three-month success question directly. If your manager rarely talks about direction ask one question that forces it. At your next 1:1 put it plainly: what does success look like for the team in the next three months? It is a fair question hard to deflect and the answer is the direction you have been missing. A manager who has never raised it will usually answer when asked even if briefly.	1	1014
762	Compare aims with one peer and find the gap. When everyone seems to be working to different aims the easiest fix is at peer level rather than above. Ask one colleague what they think the team is trying to achieve right now and compare to your version. The gap if there is one tells you something the leadership conversation has not. Even two people aligned on what matters is a smaller version of the team alignment you are missing.	1	1015
769	Tell your manager what the shaping is doing. A manager who shapes your work around your career goals is doing real work to do it and they will keep doing it when they can see it is landing. After a piece of work that was clearly chosen with your goals in mind tell them so: this one is moving me toward X. Naming the connection makes the shaping deliberate on both sides rather than just on theirs.	1	1025
770	Write the throughline from this role to where you want to go. A role that builds toward where you want to go is the right setup and you can make it sharper by writing the throughline out. Once a quarter sketch it: this role is teaching me X and Y which gets me ready for the next thing. The exercise turns natural drift into a clear arc and gives you something specific to point to when an opportunity does come up.	1	1026
771	Make the next deliberate steer this month. Steering your tasks toward your ambitions is a habit that fades unless you keep choosing. Pick the next steer now. Look at what is coming this month and identify one task you can claim shape or push for because it builds toward where you want to go. One conscious move a month adds up to a deliberate career rather than a passive one shaped by whoever is handing out work.	1	1027
772	Spell out exactly where you want to go in three years. Managers cannot factor in career goals they have never been told in plain terms and a vague I want to grow is not enough to act on. Spell it out. Write where you want to be in three years specifically: the role the kind of work the level of responsibility. Share that with your manager at the next 1:1. Specific is something they can shape work around. Vague is something they cannot.	1	1029
773	Reclaim one hour a week for goal-aligned work. When day-to-day work pulls you away from your goals the size of the pull is usually less than it looks. One hour a week deliberately spent on something that builds toward where you want to go changes the trajectory. Pick the hour protect it like a meeting and use it for the work nobody is going to ask you to do. Small drift compounds. So does small steering.	1	1030
774	Audit one week's tasks against your ambitions. Waiting for someone to help line up your work with your ambitions is a wait that often does not end. Do the audit yourself. List the tasks you actually spent time on last week and against each mark whether it built toward where you want to go. The gap is the conversation. The audit gives you something concrete to take to your manager or to act on alone.	1	1031
781	Use the shared channel to ask outside your team this week. Shared tools that make reaching other teams easy only pay back when you actually reach across and most of the cross-team channels people have access to sit unused. Use one this week. Post a question ask for a hand or just check in with someone in another team. The tool was built to make this easy. The habit of using it is what makes it stick.	1	1041
782	Turn the encouragement into one concrete partnership. Cross-team collaboration being encouraged is the green light. What matters is whether anyone walks through it. This month identify one specific person or team you should be working more closely with and start one piece of joint work however small. Encouragement turns into culture when actual partnerships get built. Otherwise it stays a slogan that nobody uses.	1	1042
783	Keep your cross-team contacts warm this quarter. Knowing who to go to in other teams is an asset that decays quickly when it goes unused. Maintain it. Once a quarter take fifteen minutes to ping your most useful cross-team contacts: a quick check-in a thank-you an interesting forward. Warm contacts respond when you actually need them. Cold ones make you regret not having maintained the relationship before you had a problem.	1	1043
784	Build the relationship the missing tool would replace. When the tools to work across teams do not exist the alternative is the relationship. Pick one team you regularly need to work with and find one person in it who you can build a real working relationship with. A direct line to one well-placed contact replaces a lot of missing infrastructure and unlike the tools it is something you can build this month without anyone else's approval.	1	1045
785	Cross the silo one introduction at a time. Silos hold at the org level long after they have stopped holding at the personal level and the personal level is where you can work. Pick the silo that costs you most and find one person in that team to have a coffee with. The silo does not move. The relationship does and the work that needed to flow across it starts flowing through your direct line instead of the official one.	1	1046
786	Get the answer informally before the formal channel. When rigid channels slow cross-team work the workaround is doing both: get the answer informally then put it through the formal channel for the record. A direct message to the right person gets you the working answer in minutes. The official channel can catch up later when it needs to. Trying to do everything by the rigid channel is what gives you the slowdown.	1	1047
793	Name your contribution after the decision lands. When your suggestions shape decisions visibly the link is most useful when it gets named. After a decision that took your input say so briefly in the next conversation: glad to see X went the direction we talked about. It is not bragging. It keeps the connection between your input and outcomes visible to others which makes future suggestions more likely to be picked up too.	1	1057
794	Track what you raised so follow-up stays easy. Managers who follow up on points you raise are doing real cognitive work to do it and they do it better with light support. Keep a short list of what you have raised and when. Every few weeks when something feels stuck you can mention it cleanly: you said you would look at X has anything moved? Tracking turns goodwill into follow-through and keeps the rhythm strong.	1	1058
795	Tell the decider you saw your input show up. Seeing your input reflected in outcomes is one of the most sustaining experiences at work and you can make it last by sharing the recognition back. Tell the person who made the decision that you noticed your input landed: thanks for taking X on board it shaped the outcome. The acknowledgement makes them more likely to include input again from you and from others.	1	1059
764	Review what the plan has actually changed for you. A tracked development plan only matters if it is producing change rather than just being updated. Every month or so ask yourself one question: what can I do now in my actual work that I could not before the plan started? If the answer is little the plan needs sharpening. If the answer is real that is the proof you should be carrying into the next review.	1	1018
765	Apply each piece of self-driven learning at least once. Actively seeking out courses or reading is one half of growth. The other half is using it. Set a simple rule for yourself: every piece of learning you pick up has to land in at least one real task before you move to the next. Without that rule learning becomes consumption and consumption looks productive while changing very little about how you actually work.	1	1019
766	Pick the task that forces you to learn something. When day-to-day work crowds out learning stop treating learning as a separate thing. Fold it in. Look at what you have to do this week and choose or volunteer for the one task that will force you to learn something new. The workload is the same. You have just selected the part of it that also develops you which is how learning survives a busy job.	1	1021
767	Bring a draft plan instead of waiting for one. Waiting for your manager to start a development plan usually means waiting. Bring one yourself. Write a short specific draft two or three skills to build over six months with one concrete action against each and take it to your next 1:1. It is far easier for a manager to react to a draft than to invent a plan from scratch and you will likely get most of it agreed in one conversation.	1	1022
768	Book a recurring twenty-minute slot in your calendar. Development loses to other work because it has no deadline while everything else does and it will keep losing that fight as long as it is left to spare time. Book a recurring twenty-minute slot in your calendar this week treated like any other meeting you would not skip. Small and scheduled produces more growth over six months than large and someday ever has.	1	1023
775	Supply an honest opinion the next time one is invited. Leaders inviting honest opinions only matters if someone actually offers them and most people stay quiet on the assumption that the invitation is a courtesy. Take it at face value. The next time honest input is asked for in your hearing supply one specific honest view. Accepted invitations get repeated. Unaccepted ones quietly disappear and the openness with them.	1	1033
776	Bring one candid view to a team conversation. A team that treats candid views as normal stays that way because individuals keep supplying candid views not because of a rule. Add yours. This week in one team conversation say the candid thing you would normally edit out: a real concern a disagreement a question you have been holding. The norm holds as long as people keep using it.	1	1034
777	Disagree out loud when you actually do. A manager who makes it safe to disagree only finds out it worked when people actually disagree and many stay quiet out of habit even when the safety is real. Use it. Next time you actually disagree with your manager say so plainly and explain why. The safety was built for that moment. Banking it for later means it never gets tested and what does not get tested fades.	1	1035
778	Lead with a question rather than a verdict. When honest opinions get shut down the shutdown often hits the form more than the content. Reframe the opinion as a question or a data point: I am seeing X am I reading it right? The same view presented as something to think about rather than a verdict is much harder to bat away and gets you the conversation that a flat statement would have closed.	1	1037
779	Be the first to say what others are thinking. Teams that avoid saying what they really think usually have several people thinking the same thing and waiting for someone else to say it. Be that someone. In the next meeting where the elephant is in the room say the unsaid thing calmly and specifically. You will rarely be the only one who thinks it. You may be the only one prepared to say it first.	1	1038
780	Ask one question before disagreeing. When disagreeing with your manager feels risky lead with a question rather than the disagreement. Help me understand the call here. Or: what am I missing? You will often get the reasoning and sometimes the reasoning is enough to change your view. When it is not you have a much safer footing to disagree from because you have already shown you listened.	1	1039
787	Put one piece of the feedback into next week. Specific and useful feedback only changes you if you act on it and the gap between received and applied is where most feedback dies. Pick the single most useful piece from your last conversation with your manager and decide where you will use it this week. One feedback point applied beats five remembered and your manager will see the change which makes the next round more useful too.	1	1049
788	Bring one specific thing you want feedback on. Regular feedback conversations work both ways and the conversation gets sharper when you ask for feedback on something specific rather than letting your manager pick. Before the next one decide one piece of work or one behaviour you want their read on. Asking for targeted feedback gets you targeted feedback. Open-ended invitations tend to get general answers that are harder to act on.	1	1050
789	Try one action within the week and report back. Feedback that comes with clear actions to try is what most people wish they had and the value is lost if you do not actually try them. Commit to one within the week and tell your manager you did. Reporting back keeps the loop closed and trains them to keep giving feedback in the form you can use rather than slipping back to vague comments without actions attached.	1	1051
790	Ask one specific feedback question this week. Managers often skip giving feedback because no one has asked for it and what you ask for is what you get. Make the question specific. At your next 1:1 ask one of these: what is the one thing I should do more of and what should I stop. Specific questions get specific answers. How am I doing gets the answer of fine which is not feedback you can act on.	1	1053
791	Propose a one-question feedback rhythm yourself. Waiting for a regular feedback rhythm to appear usually means it never does. Propose one yourself. Tell your manager you would like them to give you one piece of feedback at every 1:1 however small. A standing rhythm is easier to keep than a separate review you have to find time for and it stops months going by where you have no idea what your manager is seeing.	1	1054
792	Push for the specific behind the vague. Vague feedback is usually a starting point and most managers will sharpen it if asked. Push back gently in the moment: can you give me a specific example or can you tell me what you would have wanted me to do differently? You are not arguing with the feedback. You are asking for the bit you can act on which is the part the original sentence left out.	1	1055
796	Tie your next input to an explicit ask. Input gets heard and ignored most often when it arrives without a clear ask attached. Add one. The next time you raise something say what you want done with it: I think we should X can we decide on this by Friday? An open observation is easy to file away. A specific request with a date makes ignoring it a deliberate choice rather than a default.	1	1061
797	Show the cost of not acting on what you raised. Managers act on what you raise more readily when the cost of not acting is visible. Next time you raise something attach the consequence: if we do not handle X by next week here is what happens. It is not a threat. It is the information your manager needs to prioritise it against everything else competing for their attention. Vague concerns get deprioritised. Costed ones get acted on.	1	1062
798	Get your input in before the decision is shaped. Decisions that go ahead regardless of your input usually take their shape before you weigh in by which point changing them is much harder. Move earlier. Find out who is shaping the next big decision and supply your input while it is still being formed rather than after. Input at the formation stage moves things. Input after the shape is set rarely does.	1	1063
805	Act on one thing from the conversation this month. A discussion about your next step only matters if something moves because of it and most career conversations end with good intent and nothing further. Pick one thing from the conversation a skill to build a project to take on a person to meet and start on it this month. Acting closes the gap between the talk and the step and gives you something concrete to bring to the next conversation.	1	1073
806	Bring something different to each career conversation. Career conversations that happen on a regular basis lose their value when they turn into the same conversation. Keep yours moving. Before each one decide one new angle to bring: a fresh question an updated goal a new piece of information about what is possible. Variety is what stops a useful rhythm from becoming a check-the-box meeting where neither side learns anything new.	1	1074
807	Review the progression plan on your own schedule. An agreed progression plan only delivers if you work it actively and waiting for your manager to schedule the review can mean it slips. Put a recurring fifteen-minute slot in your own diary monthly to check progress against it. The plan was agreed together. Reviewing it is your responsibility as much as theirs and your manager will be impressed when you bring updates rather than waiting to be asked.	1	1075
808	Book a future-focused conversation separately. If career conversations never come up in your 1:1s they may never come up at all because regular 1:1s tend to fill with operational topics. Book a separate thirty-minute slot specifically about your future framed that way: I want to talk about where I am going next. A meeting with that title forces the conversation that the operational drift was preventing.	1	1077
809	Request an interim career check-in this quarter. Annual reviews are too infrequent to actually shape a career and waiting for them means most of the year passes with no direction. Ask for an interim career check-in this quarter framed cleanly: I would like a thirty-minute conversation specifically about my development. Most managers will agree if asked. Few will offer it without being asked which is the gap you can close yourself.	1	1078
810	Draft your own next-step plan in one page. Waiting for someone else to create a plan for your next step usually means waiting indefinitely. Draft one. On a single page write what the next step is the three or four things you need to develop to be ready for it and a rough timeline. Take it to your manager. A draft on the table generates a useful conversation. A wish without a draft generates polite nods and little else.	1	1079
817	Flag the next bit of bloat before it sticks. Streamlined and sensible processes do not stay that way without effort because every problem tempts someone to add a step. Be one of the people who notices early. When you see the first sign of unnecessary extra steps creeping in name it out loud even informally: do we actually need this part now? Lean processes survive when people protect them rather than when they are simply designed well.	1	1089
818	Share the tool setup that works with one colleague. Tools that fit the way you work usually do because you have configured them well over time and that knowledge sits with you. Share it. Pick one colleague who would benefit and show them the setup you use: this is how I have it configured this is what saves me time. You lose nothing. They gain something. And the practice of optimising tools spreads from individual to team.	1	1090
819	Deliver well so the freedom is never up for question. Freedom to work in the way that suits you holds as long as the work keeps landing. Protect that. The single best way to keep autonomy is to make your delivery so reliable that nobody has any reason to look at how you work. Output is what bought you the freedom. Output is what keeps it. Lose the output and the freedom is the first thing that gets reviewed.	1	1091
820	Document your workaround for the next person too. Clunky processes have shortcuts that most people figure out individually and never share. Be the one who shares. When you find a workaround that turns a painful process into a manageable one write it down somewhere others can find it. You save yourself the next time. You save colleagues every time. And the documented shortcut sometimes becomes the official one when someone in a position to fix things sees it.	1	1093
821	Spend one hour configuring the tool that fits worst. Tools rarely fit how you actually work out of the box and most people accept the default and complain about it. Pick the tool that fits worst. Spend one hour this week customising it: keyboard shortcuts views notification settings whatever you can change. An hour of configuration saves itself back many times over and it is one of the few things about your tools you fully control.	1	1094
822	Challenge one rigid rule with evidence not opinion. Rigid rules sometimes give way to evidence even when they resist opinion. Pick the rule that costs you most. Track for two weeks what it forces you to do and what it costs in time or quality then take the data to whoever can change the rule. Specific evidence about a specific cost is much harder to dismiss than a general complaint and sometimes the rule moves.	1	1095
829	Recognise one colleague's work this week. A team where recognition is a regular habit stays that way only as long as people keep doing it. Be one of them. This week recognise one colleague's work specifically and publicly in your team channel in a meeting in person somewhere others can see. The habit holds when the supply of recognition keeps coming from across the team rather than just from a few designated people.	1	1105
955	Be the one who names the difficulty in the next team meeting. Openness is a culture maintained by examples. If something is hard and you're sitting with it surface it briefly. You're not seeking sympathy you're refreshing the norm so that the next person to struggle has a clearer doorway in.	1	1273
799	Tell the person who gave it what changed. Turning feedback into concrete change is the rare half of the loop and closing it makes the next feedback better. Tell whoever gave you the feedback what you did with it: based on what you said. I changed X and here is what happened. The loop closed means more feedback comes back to you and it comes back sharper because the person knows you act on it.	1	1065
800	Bring a real situation to your next coaching moment. A manager who coaches you on how to improve gives more when you bring them real material to coach on. Before the next 1:1 pick a specific situation you handled this week and bring it: here is what I did here is what I am unsure about. Coaching grounded in actual work is more useful than coaching on general principles and you make it possible by supplying the material.	1	1066
801	Pick how you will build the next skill not just what. Knowing the next skill to build is the easy half. The hard half is how you will build it and that choice separates feedback that grew you from feedback you forgot. Pick a method: a project to try it on a book or course a colleague to learn from. Without the method the named skill stays a goal. With it the next feedback round can be on whether the skill stuck.	1	1067
802	Reverse-engineer the specific behind the vague. Vague feedback often points to something specific that the person could not articulate. Reverse-engineer it. Ask yourself: what work or moment likely prompted that comment and what about it might have triggered the feedback? You will not always be right but your best guess at the specific behind the vague is much more useful than the vague feedback itself and you can confirm it next time you talk.	1	1069
803	Ask what would have made the rating higher. When your manager gives ratings but no coaching the coaching is often available if you ask the right question. After the next rating ask one thing: what specifically would have moved this from where it landed to the next level up? That question reframes a verdict into actionable guidance and most managers can answer it even when they cannot offer coaching unprompted.	1	1070
804	Ask one trusted peer for one honest read. When you have not had useful feedback to act on the gap can be filled outside your manager. Pick one trusted peer who has seen your work and ask one specific question: what is the one thing I do well and what is the one thing I should change? Peer feedback is not a substitute for the manager kind and it is far better than none and far easier to get than waiting indefinitely.	1	1071
811	Share your own mistake openly to keep the norm strong. A culture that treats mistakes as learning rather than blame stays that way when individuals keep modelling it. Add yours. Next time you make a real mistake share it openly in your team: here is what I got wrong here is what I learned. The culture holds when individuals supply the behaviour rather than when it is announced. One person sharing honestly invites others to do the same.	1	1081
812	Mirror the modelling in your own work. Leaders owning their mistakes openly is a signal and signals lose strength if they are not mirrored down the chain. Be one of the mirrors. When you make a mistake in your own work own it the same way: here is what I got wrong here is what I have changed. The behaviour from the top only becomes the culture when people at every level start matching it.	1	1082
813	Bring the next slip-up as soon as you see it. A manager who responds calmly when you slip up has built the conditions for you to bring problems early before they grow. Use them. Next time something goes wrong tell your manager the moment you notice rather than when you have to. Early information is what calm response is for. Saving slip-ups until forced to disclose them wastes the very safety that makes things fixable.	1	1083
814	Document the reasoning behind every risky call. Where mistakes get punished the protection is documentation: calm and quiet rather than defensive. When you make a risky call write down the reasoning: the information available the call I made the why. If it goes wrong you have a record that explains the thinking. Most blame cultures soften when the alternative to blaming is reading a clear account of what happened and why.	1	1085
815	Model the openness at your own level. When no one senior admits getting it wrong the openness still has to start somewhere and your level is one place it can. Own your own mistakes openly in your own work: in retros in 1:1s in casual conversations. You will not change the senior pattern alone and you may shift the team around you and that is the part of the culture you actually work inside every day.	1	1086
816	Bring the error with the fix attached. Fear of how your manager reacts to errors shrinks when you control the framing. Bring the error with the fix attached: here is what went wrong here is what I am doing about it here is what I need from you. The same news presented with a path forward is much easier for any manager to hear even a difficult one. Naked errors generate reactions. Errors with plans generate decisions.	1	1087
823	Defend the slack before it gets filled. A balanced workload that leaves room to think is fragile because slack looks like spare capacity to anyone allocating work. Defend it. When new work lands do not absorb it automatically. Ask what should come off to make room. The slack you defend is what gives you the thinking time. The slack you let get filled is what turns balanced into overwhelmed by the end of the month.	1	1097
824	Bring early signals when realism starts to slip. Realistic priorities held jointly with your manager work as long as both of you have a clear read on what is actually possible. Be the early warning. When you can see a deadline slipping or new work threatening the balance flag it before it becomes a problem. Early signals keep realism realistic. Late signals turn a managed workload into a crisis even with a manager who would have helped.	1	1098
825	Resist filling the slack with execution work. Slack you have protected in your week only does its job if you use it for thinking rather than for catching up on execution. The pressure to refill it with tasks is constant and small. Resist it. When the slack arrives take a walk write down what is bothering you look at next month's plan: anything that is not the next thing on the list. The slack was bought for exactly that use.	1	1099
826	Find the one task most worth dropping. A workload that is too full to think in does not get fixed by working harder. It gets fixed by dropping something. List what you are doing this week and find the one task that honestly contributes least to what matters. Then drop it or drastically shrink it or hand it back. One real drop opens up the thinking room that fifty productivity tweaks would not.	1	1101
827	Propose the prioritisation rather than wait for it. When your manager does not manage priorities down with you propose the prioritisation yourself. Send a short list of what you are working on this week and your suggested ranking with one question: is that right or do you want me to swap something? Most managers will tweak rather than rewrite and the act of proposing forces a decision that the absence of proposing was not going to produce.	1	1102
828	Unbook one hour this week for thinking. A calendar with no room to reflect is usually a calendar you booked even if it filled with things that felt necessary. Unbook one hour this week and put thinking in its place. Decline a meeting where your presence is optional. Reschedule something that can move. The hour is recoverable when you treat thinking as a thing the calendar should make room for rather than the thing the calendar happens to.	1	1103
835	Revisit one technique from the training in a month. Training that matched what your role needs only sticks if you keep using it past the week it ended and most training fades because it never gets revisited. Block twenty minutes a month from now to revisit one technique: what was the move and am I still using it? The revisit turns a useful course into a permanent piece of how you work instead of a memory that slowly fades.	1	1113
836	Tell whoever made the immediate application possible. Getting to apply training straight away is what makes it stick and most training fails this test. Tell whoever made the application possible your manager the trainer the project lead that the timing was what made the learning land. The acknowledgement reinforces the practice of pairing training with immediate work and increases the chance the next round is also set up the same way.	1	1114
837	Report back to your manager on what landed. A manager who helps you put learning into practice does the work that turns a course into actual capability and that effort deserves a closing of the loop. Tell them what landed: the training you helped me apply here is what changed. Closing the loop reinforces the practice of pairing learning with work and makes the next round more likely to be set up the same useful way.	1	1115
838	Mine the irrelevant training for one transferable insight. Training that was not relevant to your work usually still contains one or two transferable insights even if the headline content missed. Find them. Look back at the training and identify the single thing that could apply to your work even adjacent. Then feed back honestly to whoever organised it: this part landed this part did not here is what I would have valued more. Better feedback shapes better training.	1	1117
839	Create a small project to apply the training to. Training you never got to apply fades fast and waiting for the right project to come along usually means it never does. Create one. Even a small piece of work twenty hours real or pretend that forces you to use what was covered. Application makes the knowledge stick. Without it the training was an event that produced a certificate and not much else.	1	1118
840	Teach the training to one colleague to embed it. Learning sticks when you teach it rather than just when you receive it. With no follow-up to embed the training become the follow-up yourself. Pick one colleague who did not attend and walk them through the most useful idea: here is what we covered here is how I am thinking about using it. Explaining locks the learning in and you have given a colleague a free benefit at the same time.	1	1119
847	Ask about a colleague before talking about yourself. A team that takes time to understand each other does it because individuals reciprocate not because of a process. Add yours. In your next informal conversation ask one genuine question about a colleague's situation before you talk about your own. Understanding works in both directions and the team feels understanding because individuals keep offering the asking rather than just receiving it.	1	1129
848	Bring something real to the listening when you can. A manager who really listens to where you are coming from gives more value when you bring something real to listen to rather than just status. Save the listening for what matters. When something is genuinely weighing on you or you are wrestling with a hard call bring it. A listening manager applied to substance moves things. A listening manager applied to updates becomes a status meeting with empathy.	1	1130
849	Revisit how one teammate works this quarter. Investing in knowing how each other works pays back when the knowledge is current and people's preferences change as their work and life change. Pick one teammate this quarter and ask casually: has anything changed about how you like to work? You will pick up small updates that prevent friction and the asking itself is part of what keeps the team understanding intact.	1	1131
850	Pick one colleague to genuinely know first. When the team does not take time to understand each other broad culture change is slow. One relationship is fast. Pick one colleague who could become someone you genuinely understand and who genuinely understands you. Invest in that one relationship: coffees questions real conversation. One person who sees you clearly gives you most of what a team that did would and is within your control to build.	1	1133
851	Send the important things in writing first. When your manager does not really listen in conversation send the important things in writing first. A short clear written message gets read and processed in ways that an in-person conversation never does and you have a record. Talk over it after they have read. The listening problem is harder to fix than the channel and writing is one channel where their attention is much more reliable.	1	1134
852	Volunteer the context you wish people had. When people make assumptions about you instead of asking the way to correct them is rarely to wait for the question because the question is not coming. Surface the context yourself: I want you to know X about how I work or about what is going on. Volunteered information overrides assumption. Waiting for the assumption to be checked usually means it never is and the wrong picture sets.	1	1135
859	Propose the next celebration when one is earned. A team that marks its wins does it because someone keeps proposing the marking not because of a calendar. Be one of those someones. When the next win lands suggest the marking: this deserves a moment can we do X. The habit holds because individuals supply the proposal. Without it even the most well-intentioned team drifts into moving straight to the next thing.	1	1145
860	Thank your manager for the call-out itself. When your manager calls out a recent success they have done a piece of work most managers skip and it deserves a small acknowledgement back. Tell them briefly: thank you for naming that it meant something. The acknowledgement makes them more likely to do it again for you and for others. Unrecognised recognition gets quietly dropped from a manager's repertoire even when it landed.	1	1146
861	Share one win with one person to make it real. Noting your own wins is the foundation and they land deeper when you say one out loud to one person. Pick a colleague a partner a friend and tell them briefly what went well. Spoken wins anchor differently from noted ones. The week ends and the win has a witness which makes it part of the record rather than something you privately observed and forgot by Monday.	1	1147
830	Accept the acknowledgement without deflecting. When your manager acknowledges your work the temptation is to wave it off: oh it was nothing. Resist that. A simple thank you that meant a lot both lands the recognition and signals that you welcome it which makes them more likely to do it again. Deflected acknowledgement teaches the giver that the giving did not land and reduces the chance of the next round.	1	1106
831	Pass the recognition forward to another colleague. A colleague thanking you for something is a small piece of culture you can grow by passing it forward. Within the week deliberately thank a different colleague for something specific they did. The chain costs nothing takes a minute and is how peer recognition becomes a norm rather than an occasional act. The team you want to work in is the one you keep building one acknowledgement at a time.	1	1107
832	Make your work visible in a Friday note. Where good work goes unnoticed the gap is usually visibility rather than quality. Close it yourself. Send a short note at the end of each week two or three lines summarising what you completed and what shifted because of it. Send it to your manager or wider where appropriate. Visibility you create gets noticed. Visibility you wait for usually does not.	1	1109
833	Tell your manager what you would like noticed. Managers who rarely acknowledge effort sometimes simply do not know what is worth acknowledging because your work has been invisible to them. Tell them once plainly. At your next 1:1 mention one thing you are particularly proud of recently and what it took. You are not fishing for praise. You are giving them the information they need to acknowledge work they would have missed.	1	1110
834	Start the thanking culture yourself with one specific thank you. A team with no culture of thanking each other still has individuals who notice good work and the culture starts when one of them says so out loud. Be that one. This week thank one colleague specifically and publicly for something they did where others can see. One genuine specific thanks does more to start the culture than any team-wide policy ever has.	1	1111
841	Scan the openings once a fortnight even when settled. Internal roles being openly advertised only helps if you look and most people stop looking once they are settled. Build a fortnightly habit of scanning even when you are not actively job-hunting. You will notice the kinds of moves people make the language used to describe roles you might want and occasionally the role that quietly fits. The cost is five minutes. The signal is much bigger.	1	1121
842	Apply for the next fit while the encouragement is real. A manager who encourages you to apply for moves is rarer than it should be and the encouragement loses value if you never take it up. The next time a role fits apply. The application itself even unsuccessful teaches you what the bar is and signals that you are serious. The encouragement was meant to be used rather than to sit as a comforting fact about your manager.	1	1122
843	Use the time and backing without burning the trust. Being given time and backing to pursue openings rests on trust that you will not abuse it and using it well is what keeps it available. Be transparent: tell your manager what role you are pursuing and how much time you need. Deliver on your current work despite the pursuit. The backing is on loan from your reputation. Use it in a way that keeps the loan available for the next time.	1	1123
844	Build a small intelligence network of people who know. Where internal opportunities are not shared openly the openings still exist. You just have to find them. Build a small intelligence network: two or three people across the org who tend to hear about moves before they are posted. Keep those relationships warm. Internal moves usually go to people who knew about the role before it was posted and being in the loop is something you can build deliberately.	1	1125
845	Build a direct relationship with one hiring manager. A manager who discourages you from moving on does not control whether other managers want to talk to you. Identify one hiring manager whose team you would want to be on and build a direct relationship with them: a coffee a question about their work a useful introduction. By the time a role opens they already know you. Your manager's gatekeeping becomes less relevant when the next manager has already met you.	1	1126
846	Build your own support without waiting for institutional. Without institutional support to pursue internal roles the alternative is the support you build yourself. Identify one peer who can read your application materials and one person more senior who can prep you for interviews. Ask both directly. Most people say yes when asked specifically. The support exists. It just has to be assembled by you rather than offered to you.	1	1127
853	Learn one new feature of your most-used tool. Having the right tools is the easy half. Using them well is the half that quietly compounds. Pick the tool you use most and commit to learning one new feature a month: a keyboard shortcut a view an automation. Twelve features a year on a tool you use daily changes how fast you work. Right tool plus mastered tool is a different thing from right tool plus default tool.	1	1137
854	Check your access list every six months for drift. Having the access and permissions you need is the current state. Permissions drift quietly as systems change and people move. Every six months walk through what you actually have: the tools the folders the shared resources. Anything that needs adding ask for. Anything missing raise. Drift is easy to fix when you catch it. It is much harder to fix the day you suddenly need something you used to be able to do.	1	1138
855	Stay inside the boundaries your tools handle well. Tools that rarely fail tend to stay reliable as long as you use them inside what they are built for. Notice the boundary. When you are tempted to push a tool past its design into bigger files edge cases or hacks ask whether the better move is a different tool. Reliability is partly the tool and partly how you use it and the second part is the one you control.	1	1139
956	Bring one real difficulty to your next 1:1. Treat the safety as a renewable resource. Pick something you're actually wrestling with rather than a polished version. Sharing the unfinished thinking is what keeps the manager's trust alive. Polished problems train them to expect you don't need them at all.	1	1274
643	Turn one idea from that conversation into a specific action item by end of day.	1	643
644	Share one improvement idea from that discussion with the rest of your team.	1	644
645	Book a follow-up conversation to review progress on what you both agreed to try.	1	645
646	Message your manager today: 'I have one idea for improving how we work. Can we chat?'	1	646
647	Before your next meeting write down one specific problem and a proposed solution.	1	647
648	Note one recurring issue that keeps coming up and suggest a small experiment to fix it.	1	648
856	Make the request specific with the cost attached. Missing tools you need to do your job get acquired when the request is specific and costed. Vague: I need better tools. Specific: I need this particular tool here is what it would do for me here is the time it would save per week. The second is easy to approve. The first is easy to nod at and forget. Specificity is what moves the request from wish to budget line.	1	1141
857	Name the permission the reason and the granter. Access and permission gaps drag on when the request is fuzzy. Sharpen it. The next time something blocks you write a single request with three things: the specific permission you need the specific business reason for it and the specific person who can grant it. Then send it directly. Most permission requests stall on missing one of the three. Send all three together and the request often moves the same day.	1	1142
858	Document the cost of slow tools for two weeks. Slow or unreliable tools get replaced when someone makes the cost visible and you are the person best placed to do it. For two weeks log when a tool slows you down or fails with the time lost. Two weeks of data turns a complaint into a business case and a business case is much harder to defer than the complaint your manager has been hearing for months without numbers attached.	1	1143
865	Raise the next thing in the form leaders can act on. Leaders who act on what people raise can only act when the thing raised is actionable and a lot of feedback comes in forms that are hard to do anything with. Frame yours well. The next time you raise something state the issue the impact and one suggested action. Actionable input gets acted on. Vague observations get noted and not much else even by leaders who genuinely want to respond.	1	1153
866	Respond to the response so the loop stays open. Being told what happened to your feedback is the rare half of the loop. Most places give input and never hear back. Keep yours alive. When you get told what happened respond even briefly: thank you for telling us that makes a difference. The loop holds when both sides treat it as worth maintaining. Unrecognised closing makes the closing stop happening.	1	1154
867	Speak up on the next thing while the channel is open. Having seen real change come from speaking up is the strongest proof that the channel works and the natural temptation is to stop because you have spent the credit. Do not. The channel is open partly because people keep using it. Pick the next thing worth raising and raise it. The conditions for change are easier to keep than to rebuild after they have gone quiet.	1	1155
868	Raise it with whoever can actually act. When leadership ignores what people raise the input is going to the wrong audience. Find the right one. For most issues there is someone closer to the work who can actually act your manager a peer in another team a process owner. Take it to them with a clear ask. Leadership might still be ignoring you and the problem starts moving because you took it to someone who could move it.	1	1157
869	Re-raise it with the date you first raised it. Feedback that disappears with no response is usually buried rather than deliberately ignored. Re-raise it with the date attached: I flagged this on the fifth has anything moved? Adding the date turns the second mention from nagging into legitimate follow-up. Most things get acted on the second time around when the time elapsed since the first raise makes it harder to keep deferring.	1	1158
870	Fix what you can in your own work while the system holds. When nothing changes at the system level no matter what you say the energy spent shouting at it is energy not spent on what you can change. Identify the part of the problem that sits inside your own work and fix that. The system stays broken meanwhile and your day improves. A local fix done well sometimes becomes the proof point that changes the system later.	1	1159
877	Debrief yourself when the stretch assignment ends. A genuine stretch assignment is most valuable when you extract what was learned and most stretch work ends without anyone closing the loop. Do it yourself. When the assignment finishes write a short debrief: what was hard what worked what I can now do that I could not before. The work happened. The debrief is what turns it into capability you carry forward into the next stretch.	1	1169
957	Reflect on a past challenge you admitted and what it changed. The lesson is worth re-examining. If admitting difficulty hasn't cost you you've got data others don't. Note in a private journal what made it safe and what came of it. That memory steadies you next time the stakes feel higher.	1	1275
365	Replace the guess with one direct question, Guessing at the reason behind a change wastes energy and usually guesses wrong. Stop guessing and ask. Pick the change that is bothering you most and put one plain question to the person who made it or announced it: what is the thinking behind this? Most people will explain if asked directly. The guessing continues mainly because nobody asks the question out loud.	1	365
366	Confirm your understanding before you build on it, Rework happens when you act on an unclear decision and find out later you read it wrong. Catch that before it costs you. Before you start the work, write your understanding of the change in two lines and send it to the relevant person: this is what I am taking from it, correct? Thirty seconds of confirmation is far cheaper than a day of redone work.	1	366
367	Write down the new knowledge so it does not fade, Knowledge picked up through work is the easiest kind to lose, because it never felt like studying. Pin it down. Take the most useful thing you have learned recently and write it as one or two plain lines somewhere you will find it again. The note is what separates something you genuinely know from something you happened to encounter and will half-remember in a month.	1	367
368	Use the next practice chance on your weakest part, Chances to practice are most valuable when you point them at what you are not yet good at, not what is already comfortable. Next time you get one, deliberately use it on the part you find hardest. Practising the easy bits feels productive and changes little. Practising the weak bit is mildly uncomfortable and is where the actual skill is built.	1	368
369	Tell your manager which learning is paying off, Learning that feels relevant to your role is more fragile than it looks, because what you get offered is set by other people's guesses. Protect the fit. Tell your manager which recent learning has actually been useful and why. Naming what is landing makes it far more likely the next opportunity is pointed the same way rather than drifting off target.	1	369
370	Make space by learning inside a current task, When there is no obvious space to learn something new, stop looking for a separate space and fold it into the work. Pick one current task and do a part of it in a way that requires learning something: a new method, a tool feature, a technique you have read about but never used. The space does not have to be found. It can be made inside what you already do.	1	370
371	Change one thing about a repetitive task this week, Repetitive work stops offering development when you run it the same way every time, but the repetition is not the only fixed thing. Pick one repetitive task and change a single element: do it faster, do it cleaner, automate a step, improve the output. The development moment is in the change you introduce, not in waiting for the task itself to become different.	1	371
372	Protect twenty minutes before the demands start, Learning gets squeezed out because it has no deadline and everything else does, so it always loses the moment the day starts. Move it before the day starts. Block twenty minutes early, before the demands arrive, for one specific piece of learning. Time taken at the front of the day is yours. Time you hope to find later has already been promised to something louder.	1	372
373	Write down the difference so it survives a hard week, Seeing that your work makes a difference is motivating now, and that feeling does not survive a stressful week on its own. Give it something solid. Write one specific example of the difference your work made, who it helped and how. On the days the meaning feels thin, a concrete record carries more weight than trying to remember why it mattered.	1	373
374	Tell one colleague the value you see in their work, Feeling that your contributions are valued is worth passing on, because plenty of people doing purposeful work cannot see it themselves. Pick one colleague and tell them specifically how their work matters: the difference it makes, who it helps. You know how much that recognition is worth because you are feeling it. Giving it costs a sentence and lands harder than you expect.	1	374
375	Name what makes the work feel worthwhile, Motivation that comes from worthwhile work is strong while it lasts, and it lasts longer if you understand its source. Name precisely what makes this work feel worth doing: the people it serves, the problem it solves, the craft of it. Knowing the source means that when motivation dips, you can deliberately reconnect with it rather than waiting for it to return on its own.	1	375
376	Trace one task to the person it affects, Impact is hard to see when your work hands off before you can watch what it does. Trace one piece yourself. Pick a recent task and follow the thread: who received it, what they did with it, what changed because of it. You may need to ask one person downstream. The impact usually exists. It is just past the point where your own view normally stops.	1	376
377	Ask how your work connects to a goal you care about, Work feels disconnected from meaningful outcomes when nobody has drawn the line between your task and the larger goal. Ask for the line. Pick someone who sees the bigger picture and ask directly how what you do feeds into something that matters. The connection often exists and was simply never explained. Once you can see it, the same work stops feeling disconnected.	1	377
378	Rebuild purpose from the part of the work you control, When lost purpose has drained your motivation, waiting for it to return rarely works. Rebuild it from something smaller and closer. Pick one part of your work you genuinely control and find the meaning in it: the quality you can put in, the colleague you can help, the problem you solve well. Purpose reassembles from the pieces you can act on, not the picture you cannot.	1	378
379	Use the safety on something that actually matters, Feeling safe to be honest is only worth something if you spend it on real things. This week, use it once: raise the genuine concern, give the honest feedback, admit the actual gap. Safety that only ever carries small, comfortable truths slowly stops being tested, and untested safety quietly weakens. Using it on something that matters is what keeps it real.	1	379
380	Respond with respect when someone else speaks up, Being met with respect when you speak up is what makes speaking up feel possible, and the same is true for everyone. Pass it on. Next time a colleague says something difficult or admits a problem, respond the way you would want to be met: take it seriously, with no hint of dismissal. Each respectful response makes the next person more willing to be honest.	1	380
381	Be the supportive response when someone is open, Openness being met with support is not a fixed feature of a place. It is made of how individuals react, one moment at a time. Be one of the people who makes it real. When someone is open with you about a struggle or a mistake, lead with support: what do you need, how can I help. Judgement and support are both contagious. You get to choose which one you spread.	1	381
382	Start with a low-stakes truth to test the water, Worry about consequences makes every honest thing feel like a big risk. Test the water with a small one. Pick something low-stakes and say it honestly, framed as an observation rather than a complaint, then watch how it lands. A few small, safe attempts give you real evidence about what happens here. Often the fear is larger than the reality, and only evidence shows you that.	1	382
383	Find one person you can be honest with first, When honesty feels risky across the board, you do not have to be honest with everyone to break the silence. Start with one. Identify a single colleague or manager who has earned some trust and have one honest conversation with them. A safe environment is built from individual safe relationships. One reliable person is a real foothold, and it is more than the all-or-nothing version offers.	1	383
384	Share one thing you would normally keep back, Holding back protects you in the short term and quietly costs you in trust, because people sense the distance. Close a little of it on purpose. Pick one thing you would normally keep to yourself, a view, a concern, a small uncertainty, and share it with the right person this week. Trust grows from small, deliberate disclosures. Holding everything back guarantees it stays where it is.	1	384
385	Notice what gives you clarity and keep asking for it, Having the clarity and support to perform well is not the default, so it is worth knowing what produces it. Notice the specifics: the brief that was clear, the question that was answered fast, the access you were given. Then keep asking for those same things by name on the next piece of work. Clarity you can describe is clarity you can request again.	1	385
386	Flag the next dependency before it becomes a blocker, Dependencies get handled efficiently partly because someone raises them early. Keep being that someone. Look ahead on your current work and name the next thing you will need from someone else, then flag it now rather than when you reach it. A dependency raised early is a scheduling note. The same dependency raised late is a blocker. The difference is who looks ahead.	1	386
387	Protect the conditions that keep the path clear, Steady progress without major blockers usually has causes worth protecting: good handoffs, early planning, the right access in place. Notice which conditions are keeping your path clear and treat them as things to maintain, not luck. A clear run tends to end when one of those quietly slips. Knowing what they are means you can catch the slip before it becomes a blockage.	1	387
388	Name the recurring blocker and tackle its cause, Blockers that keep recurring are usually one or two specific things, not bad luck. Find the pattern. For a week, note what actually stops you each time. One cause will likely repeat. Then tackle that single root: the approval that is always slow, the information always missing. Fixing one recurring blocker at its source clears more time than firefighting each instance as it lands.	1	388
389	Make one specific list of exactly what you need, Missing information, access, or support stays missing when the need is vague. Make it precise. Write one short, specific list: exactly what you need, from whom, by when, and why it is blocking you. Then send it to the people who can act. A clear, itemised request is hard to ignore and easy to fulfil. A general I do not have what I need is neither.	1	389
390	Look for the constraint that effort cannot fix, When strong effort still does not produce delivery, the problem is usually not effort, and adding more will not help. Look for the constraint. Step back and find the one thing genuinely in the way: a missing decision, a broken process, an unrealistic scope. Name it. A constraint you have identified can be raised and addressed. Working harder against it just costs more for the same result.	1	390
391	Turn the clear thinking into one written next step, A conversation that helped you think clearly is valuable, and clarity fades fast once you are back in the work. Lock it down. Before the day ends, write the one next step the conversation pointed to, in a single specific line. Clear thinking that becomes a written action survives. Clear thinking left in your head is usually gone by tomorrow's first interruption.	1	391
392	Act on one option this week while it is fresh, Exploring options constructively is only useful if one of them gets tried. Pick the most promising next step from that conversation and do something concrete with it this week. Options that get explored and then sit there teach the quiet lesson that these conversations do not lead anywhere. Acting on one, soon, is what keeps them worth having.	1	392
393	Use the direction before the momentum fades, Leaving a conversation with confidence and direction gives you a short window where action is easy. Use it now. Take the clearest piece of direction you got and make a start on it today, while the momentum is still there. Direction acted on quickly becomes progress. Direction left a week turns back into a vague intention, and the confidence drains with it.	1	393
394	Book the conversation instead of waiting to be offered, Space to talk through ongoing challenges rarely appears on its own. You usually have to claim it. Book fifteen minutes with your manager and name the purpose: one specific challenge you want to think through together. A short, focused, scheduled conversation is easy to grant and easy to use. Waiting for the space to open up is what keeps the challenge sitting unaddressed.	1	394
395	End the next conversation by agreeing one action, Conversations fail to produce clarity when they end without anyone naming what happens next. You can fix that from your seat. Before the next one closes, ask one question: so what is the one thing we have agreed I will do, and by when? It feels slightly blunt and it works. A conversation that ends on a named, dated action cannot dissolve into nothing.	1	395
396	Spend five minutes finding why the issue keeps returning, Issues repeat when you keep solving the instance and never the cause, because there is no reflection time to spot the pattern. Make five minutes. Next time a familiar problem comes back, before fixing it again, ask one question: what keeps causing this? Five minutes on the cause, even once, can end a problem that quick fixes have been managing for months.	1	396
397	Write down what good looks like before you start, Knowing what good looks like is a real advantage, and the picture in your head drifts as work proceeds. Anchor it. Before you start the next significant task, write the standard down in two or three concrete lines: what done and good actually means. A written definition keeps you aimed at the target through the messy middle, where a remembered one slowly blurs.	1	397
398	Confirm the priority in writing so it stays fixed, Being able to confirm priorities easily is worth using fully, because verbal agreement quietly shifts. After you confirm a priority or next step, put it in one written line back to the person: just to confirm, this is the priority, this is next. The written version is something you can both point to later, which protects you when memories of the conversation diverge.	1	398
399	Notice what created the clarity and ask for it again, Delivering confidently because expectations were clear is the system working well, and clarity is not guaranteed on the next task. Notice what produced it: a specific brief, a question you asked, an example you were given. Then ask for the same thing deliberately next time. Clarity you can trace to a cause is clarity you can reproduce rather than hope for.	1	399
400	Pin vague expectations down in writing yourself, Vague or shifting expectations cost you most when they stay verbal and unconfirmed. Pin them down. When you are given a task, write your understanding of what is expected and send it back: here is what I am taking from this, is that right? It surfaces gaps early, and if expectations later change, you have a record of what was originally agreed.	1	400
401	Ask the one question that defines success, Working without a clear picture of success means working hard with no way to know if you are aiming right. Get the picture. Ask your manager one direct question before you go far: when this is done well, what does it look like, and how will we know? A few minutes defining the target is far cheaper than a week of effort pointed slightly wrong.	1	401
402	Check alignment at the start rather than the end, Wasted effort from misalignment is almost always discovered at the end, when it is too late and too expensive to fix. Move the check forward. On the next significant task, confirm alignment before you start and again at a quick early checkpoint: are we still pointed the same way? Catching a small misalignment early costs minutes. Finding it at delivery costs the whole job.	1	402
403	Write down the new knowledge so it does not fade, Knowledge picked up through work is the easiest kind to lose, because it never felt like studying. Pin it down. Take the most useful thing you have learned recently and write it as one or two plain lines somewhere you will find it again. The note is what separates something you genuinely know from something you happened to encounter and will half-remember in a month.	1	403
404	Use the next practice chance on your weakest part, Chances to practice are most valuable when you point them at what you are not yet good at, not what is already comfortable. Next time you get one, deliberately use it on the part you find hardest. Practising the easy bits feels productive and changes little. Practising the weak bit is mildly uncomfortable and is where the actual skill is built.	1	404
405	Tell your manager which learning is paying off, Learning that feels relevant to your role is more fragile than it looks, because what you get offered is set by other people's guesses. Protect the fit. Tell your manager which recent learning has actually been useful and why. Naming what is landing makes it far more likely the next opportunity is pointed the same way rather than drifting off target.	1	405
406	Make space by learning inside a current task, When there is no obvious space to learn something new, stop looking for a separate space and fold it into the work. Pick one current task and do a part of it in a way that requires learning something: a new method, a tool feature, a technique you have read about but never used. The space does not have to be found. It can be made inside what you already do.	1	406
407	Change one thing about a repetitive task this week, Repetitive work stops offering development when you run it the same way every time, but the repetition is not the only fixed thing. Pick one repetitive task and change a single element: do it faster, do it cleaner, automate a step, improve the output. The development moment is in the change you introduce, not in waiting for the task itself to become different.	1	407
408	Protect twenty minutes before the demands start, Learning gets squeezed out because it has no deadline and everything else does, so it always loses the moment the day starts. Move it before the day starts. Block twenty minutes early, before the demands arrive, for one specific piece of learning. Time taken at the front of the day is yours. Time you hope to find later has already been promised to something louder.	1	408
409	Choose the next challenge for what it will teach, When meaningful challenges are on offer, the one you pick matters. Do not just take the most visible or the most comfortable. Choose deliberately for what it will build: the skill you want next, the experience you are missing. A challenge chosen for its learning develops you twice, through the work and through what it teaches. One chosen by default only gets the work done.	1	409
410	Use the support to take the stretch a bit further, Being able to stretch beyond your usual scope with support is the safest time to push, so push slightly harder than feels obvious. When you take the next stretch, add one element that genuinely tests you. Supported stretch is where growth is cheap, because the safety net is there. Playing it small inside a supported stretch wastes the conditions that make bigger moves low-risk.	1	410
411	Take the next opportunity before the momentum cools, Confidence and momentum from new opportunities have a short half-life. They fade if the gap before the next one is too long. While you are still carrying the momentum from the last challenge, put your hand up for the next. Each opportunity taken soon after the last compounds the confidence. Long gaps between them let you slide back toward the comfortable default.	1	411
412	Ask directly for the stretch you want, Stretch opportunities feel limited partly because they are given to people who have asked, not handed out evenly. Make yourself one of the askers. Tell your manager the specific kind of challenge you want and why you are ready for it. A clear, direct request puts you on the list for the next opportunity. Waiting quietly to be chosen usually means being passed over.	1	412
413	Earn the bigger work with a visible smaller win, Trust for new or interesting work is given on evidence, and if you have not been trusted with it yet, you may not have had the chance to show you can handle it. Make a chance. Ask for one small piece of more demanding work and deliver it visibly well. A concrete success is what shifts a manager's sense of what you can be trusted with.	1	413
414	Name the specific block instead of the general feeling, Feeling your growth is held back is a general sense, and general senses are hard to act on. Make it specific. Identify the one concrete thing in the way: a skill you cannot build, an opportunity you keep missing, a conversation that has not happened. A named block is something you can raise and push on. A vague feeling just sits there and drains you.	1	414
415	Raise the next concern early and with a suggestion, Being taken seriously when you raise concerns is worth using well. Raise the next one early, while it is still small, and bring a suggested fix alongside it. A concern raised early with an idea attached is easy to act on and confirms you as someone worth listening to. Concerns saved up until they are urgent are harder to address and harder to hear.	1	415
416	Share the lesson from your next mistake out loud, Mistakes being treated as learning only stays true while people actually share what they learn. Keep that real. Next time you get something wrong, name it openly and say what it taught you, where the team can hear. One person turning a mistake into a visible lesson keeps the practice alive and gives everyone else permission to do the same.	1	416
417	Use your candour to make space for someone quieter, Being comfortable with candour is a genuine asset, and you can use it for more than your own points. Use it to draw others out. When you are in a conversation, notice who is holding back and invite them in directly: what is your read on this? Your ease with speaking openly can create room for people who do not yet have it.	1	417
418	Raise the concern with its cost attached, Concerns get dismissed easily when they sound like opinions. Attach a cost and they become harder to wave off. Next time you raise one, include the concrete consequence: if this is not addressed, here is what it will cost in time, rework, or risk. A concern with a specific price tag is a business problem someone has to weigh, not just a worry they can set aside.	1	418
419	Frame the issue around the problem rather than the person, Blame and defensiveness flare when an issue sounds like an accusation. You can lower the temperature by how you raise it. Talk about the problem and the situation, not who caused it: here is what is happening, here is what I think we should do. Keeping the focus on the problem rather than a person gives everyone room to fix it instead of defend themselves.	1	419
420	Be candid once on something low-stakes, Avoiding candour protects you in the moment and slowly costs you, because problems you do not name do not get solved. Loosen it gradually. Pick one low-stakes thing and be honest about it this week, framed constructively. A small, safe act of candour gives you evidence about how honesty is actually received here, which is more useful than the caution that has kept you quiet.	1	420
421	Pass the working process on to someone struggling, A process that genuinely helps is worth more when more people follow it well. If a process works smoothly for you, find one colleague who is struggling with the same one and show them how you run it. A good process spreads through people teaching each other the practical version, not through the documentation nobody reads.	1	421
422	Note what makes approvals fast and reuse it, Getting through approvals without delay is often the result of specific habits: asking the right person, giving complete information, timing the request well. Notice exactly what is working for you and treat it as a method, not luck. Then apply the same habits deliberately to the next approval. Smooth navigation you can describe is smooth navigation you can repeat.	1	422
423	Protect the way of working that is reducing friction, When ways of working genuinely reduce friction, that is worth defending, because process tends to drift toward complexity over time. Notice which specific practice is keeping things smooth. When a change to it is proposed, you can speak up to protect what works, with a concrete reason. Good process survives when someone who benefits from it can explain why it should stay.	1	423
424	Document the one step that adds no value, A process full of unnecessary steps is hard to challenge as a whole. Challenge one step. Identify the single step that most clearly adds no value and write down what it costs: the time, the delay, the duplication. A specific step with a measured cost is something the process owner can remove. The whole bloated process is too big to argue with at once.	1	424
425	Make the decision easy to say yes to, Decisions and approvals stall when they reach someone as an open question they have to work out. Do the work for them. When you need a decision, present the options, your recommendation, and the information needed to choose, then ask for a yes or a no. A decision framed as a quick confirmation moves far faster than one framed as a problem to solve.	1	425
426	Work the slow process while something else moves, When a slow process is holding up progress, the lost time is the waiting, not the work. Stop letting it block you. When something is stuck in a slow process, start it as early as you can and switch to other work while it grinds through. You cannot speed the process up, but you can stop it from being the thing you sit and wait on.	1	426
427	Sharpen the question your reflection asks, A reflection habit you already keep is worth improving rather than just continuing. Most reflection asks how did it go, which is too broad to produce much. Swap it for something sharper: what did I do that worked and should repeat, and what cost me more than it should have. A better question is what turns a steady habit into one that actually changes how you work.	1	427
428	Turn the next reflection into one concrete change, Reflection that helps you reset and prioritise is doing real work, and it does the most when it ends in a change. After your next reflection, do not just feel clearer. Name one specific thing you will do differently because of it, and start it. Reflection that produces a change compounds. Reflection that only produces a calmer mind resets you to the same place.	1	428
429	Write the direction down so it survives the week, Taking stock gives you clarity, and clarity is the first thing a busy week erodes. Make it durable. When a moment of reflection gives you a clear direction, write it in one or two plain lines and keep it where you will see it. The note holds the direction steady when the daily noise would otherwise scatter it within a few days.	1	429
430	Shrink reflection to one question at day's end, Reflection loses to a busy week because it sounds like a session you have to find time for. Shrink it until it fits the time you already have. Before you close your laptop, answer one question: what is the most useful thing I learned today? Sixty seconds, no notebook. Done daily, that is more genuine reflection than most people manage in a quiet month.	1	430
431	Attach reflection to something already in your day, Waiting for reflection to be built into how the team works means waiting indefinitely. You do not need the routine to change, you need to attach to it. Pick something that already happens every day, the commute, the first coffee, the end of a call, and make that your reflection cue: one question, same moment, every day. The trigger already exists. Give it a second job.	1	431
432	Run a two-minute review at the end of one task, Missed learning is the quiet cost of finishing a task and moving straight to the next. Insert a small pause. At the end of your next significant task, before moving on, spend two minutes on two questions: what worked here, and what would I do differently. That short review is where the learning gets captured. Without it, the experience happens and teaches you nothing.	1	432
433	Pass timely information on to someone downstream, Getting the right information at the right time is an advantage, and someone downstream of you often gets it late or not at all. Extend the chain. When you receive something useful, take a moment to pass it to the person whose work depends on it. Good information loses value the slower it travels, and you can keep it moving past where it would otherwise stop.	1	433
434	Copy the clear update style when you send your own, Updates that are easy to understand and act on are built a specific way: they lead with what matters, they are concise, they say what to do. Notice the format that works and copy it in your own updates. The clarity you appreciate receiving is a style you can give, and your colleagues will act faster on updates that follow it.	1	434
435	Keep the communication habit that prevents the stalls, Work moving smoothly because communication is good is not automatic. It rests on habits: updates sent before they are chased, questions answered quickly, status made visible. Notice your part in that and keep it deliberate. The smooth flow you are benefiting from would slow the moment those habits lapsed, so treat your own as maintenance worth keeping up.	1	435
436	Ask for the update at the moment you need it, Delayed or murky updates leave you waiting and guessing. Stop waiting for the information to find you. Work out exactly what you need to know and when, then ask the right person directly at that point. A specific, well-timed question, what is the current status of X, gets you a clear answer now rather than a delayed and confusing one later.	1	436
437	Keep one note that holds the current picture, When information arrives scattered across channels and inconsistent, the cost is holding the real picture in your head. Stop doing that. Keep one running note for your main work and write the latest known state into it whenever something new lands, wherever it came from. That single note becomes your reliable reference, even while the channels around it stay messy.	1	437
438	Batch the chasing into one block rather than all day, Chasing clarity all day long fragments your attention and stops real work cold. Contain it. Gather the things you need clarified into one list, then chase them all in a single block at a set time, rather than one interruption at a time. Batching the chasing protects the rest of your day for actual progress, and a list of questions often gets answered faster than scattered ones.	1	438
439	Name what you are learning from the stretch, Being stretched in a healthy way is producing growth, and growth is easy to miss while you are inside it. Make it visible to yourself. Every week or so, name one specific capability the stretch is building. Naming it does two things: it shows you the progress on the hard days, and it gives you something concrete to point to when you want the next opportunity.	1	439
440	Act on one piece of guidance within the week, Guidance that helps you improve only improves anything once you use it. Close the gap fast. Take the most useful recent piece of guidance and apply it to a real task this week, then tell the person who gave it what changed. Acting quickly cements the improvement and shows the person their guidance lands, which makes them more likely to keep investing it in you.	1	440
441	Write down what you can now do that you could not, Feeling your skills move forward is motivating, and it fades because each new ability quietly becomes the normal you forget you ever lacked. Capture the movement. Spend two minutes writing what you can do now that you could not three months ago. On the flat days, that list is concrete proof you are progressing, not just a feeling that comes and goes.	1	441
442	Add a short review so challenges become learning, A hard challenge does not teach you anything on its own. The learning comes from the review afterward, and skipping that is why challenges stop translating. Add the step. When you finish a demanding task, spend five minutes on what it taught you: what worked, what you would change. The challenge supplied the experience. The review is what converts it into a skill.	1	442
443	Ask one specific person for one specific thing, Feedback and support for growth rarely arrive unprompted, because nobody knows exactly what you need. Ask precisely. Pick one person and one specific thing: would you watch how I handle X and tell me one thing to improve? A narrow request aimed at the right person is easy to say yes to, and it gets you the targeted input that waiting never delivers.	1	443
444	Pick one skill and make one move on it this week, Feeling stuck usually means development has become passive, something you are waiting to receive. Make it active again with one small move. Choose a single skill you want to build and do one concrete thing toward it this week: a task that uses it, a person to learn from, twenty minutes of practice. Stuck breaks the moment you take one deliberate step yourself.	1	444
445	Pick one progression option and start preparing, Seeing clear options for progression is only useful if you move toward one. Choose the option that fits you best and start preparing now: the skill it needs, the experience it expects, the people who should know you are interested. A path you are actively preparing for tends to open. One you are only keeping in view tends to stay a view.	1	445
446	Use the encouragement to have one real conversation, Being encouraged to explore pathways is permission, and permission only matters when used. Have one real conversation this month: ask someone in a role you might want how they got there and what it takes. Encouragement that turns into a concrete conversation moves you forward. Encouragement that stays a good feeling leaves you exactly where you are.	1	446
447	Name the role that fits and ask for the support, Support to move toward roles that fit your strengths works best when you can tell people exactly what you are aiming at. Get specific. Name the kind of role that fits you, then ask for one concrete piece of support toward it: an introduction, a stretch task, a development conversation. Specific support is easy to give. A general wish to progress gives people nothing to help with.	1	447
448	Ask someone what the next step really requires, Progression feels out of reach largely because the path is unmapped, and you cannot move toward something you cannot see. Map one step of it. Find someone a level ahead and ask plainly what their step up actually required: which skills, what experience, how long. A path you can see has a first move on it. An unclear one only has a vague distance.	1	448
449	Find one person outside the chain to ask, If your manager is not supporting you to explore new paths, they do not have to be your only route. Look wider. Find one person elsewhere in the organisation doing something that interests you and ask for fifteen minutes to hear about it. Exploration does not require permission from above. It requires one willing person to talk to, and there is usually one.	1	449
450	Spend thirty minutes finding what actually exists, Being unsure what opportunities exist usually means you have not yet gone looking, not that there are none. Go looking. Spend thirty minutes this week finding out: check internal listings, ask two well-connected colleagues, look at where people in roles like yours have moved. Thirty minutes of deliberate searching replaces a vague uncertainty with an actual list of options.	1	450
451	Make space for someone who is not at ease yet, Feeling accepted and able to be yourself is something not everyone around you has, especially people who are newer or different from the group. Use your ease to widen the room. Make one deliberate move this week to help someone less settled feel they belong: include them, ask their view, make a connection. Acceptance grows when the people who have it extend it.	1	451
452	Show empathy first when a colleague is struggling, Empathy when challenges arise is built from individual responses, not policy, and you are one of the responders. Be deliberate about it. Next time a colleague is visibly having a hard time, lead with the person before the task: a genuine question, a real offer of help. Each time someone responds that way, it becomes a little more the normal here.	1	452
453	Use the trust on something genuinely difficult, Trust to share difficult things openly is only worth having if you use it for the genuinely difficult, not just the mildly awkward. This week, if there is something real you have been holding back, share it with the right person. Trust that only ever carries easy things slowly fades. Used on something that matters, it gets stronger and proves itself real.	1	453
454	Lower the guard once with one trusted person, Being guarded keeps you safe and also keeps you slightly apart, because people connect with what you let them see. You do not have to drop the guard everywhere. Pick one person who has earned some trust and share one slightly more honest thing than usual. Connection deepens through small, chosen disclosures. A guard that never lowers guarantees the distance stays.	1	454
455	Tell one person plainly what you need from them, When empathy and understanding feel absent, people are often not unkind, just unaware, because they cannot see what you have not shown them. Give one person something to respond to. Tell a colleague you trust, plainly, what is hard right now and what would help. Understanding usually follows knowing. People cannot meet a difficulty you have kept invisible.	1	455
456	Rebuild engagement from one safe interaction, When low acceptance has worn down your confidence, withdrawing further feels safer and quietly makes it worse. Rebuild in small steps. Pick one low-risk interaction this week, a conversation with someone safe, a small contribution in a friendly setting, and engage just there. Confidence comes back through repeated small, positive contact, not through waiting until you feel ready.	1	456
457	Learn one more thing your reliable tools can do, Reliable tools usually do more than you currently use, and that unused capability is free productivity. Spend fifteen minutes on one tool you depend on, learning a feature you have never tried: a shortcut, an automation, a better view. Most people use a fraction of what their tools offer. Closing a little of that gap pays back every day after.	1	457
458	Show a colleague the system trick that helps you, If a system makes your work efficient, the same trick will help your colleagues, and most of them have not found it. Spend five minutes showing one person the specific thing that speeds you up. Your private efficiency becomes a shared one, and the colleague will usually trade you a tip you did not know. Good practice spreads person to person.	1	458
459	Know which tool earns its place and why, When technology genuinely supports your quality and speed, it is worth knowing which specific tools are doing the heavy lifting and how. Tooling decisions come around regularly, and they are often made by people who do not use the tools daily. Being able to say clearly what works and why means you can argue to keep it. People defend what they can describe.	1	459
460	Report the fault with steps and frequency, A tool problem described vaguely, it is slow, it is annoying, is easy for IT or support to deprioritise. Make it actionable. Write down exactly what you do, exactly what goes wrong, and how often it costs you time. A precise, repeatable report with a real cost attached is something someone can fix and justify fixing. A general complaint just waits in a queue.	1	460
461	Build one small bridge for the worst handoff, When systems do not integrate, the time disappears at the handoff: re-keying, copying, double-checking. You cannot fix the integration, but you can make the worst handoff reliable. Build a small bridge for it, a checklist or a template that makes the manual step quick and error-free. It will not solve the gap, but it stops the gap from costing you twice every time.	1	461
462	Turn your best workaround into a shared shortcut, If you are losing time to tool limitations, you have probably built workarounds already, scattered and informal. Consolidate the best one. Take the workaround that saves you the most and make it solid: write it down, turn it into a template or a saved process. A messy workaround done repeatedly stays a cost. A clean one becomes a shortcut you and others can reuse.	1	462
463	Sharpen the question your reflection asks, A reflection habit you already keep is worth improving rather than just continuing. Most reflection asks how did it go, which is too broad to produce much. Swap it for something sharper: what did I do that worked and should repeat, and what cost me more than it should have. A better question is what turns a steady habit into one that actually changes how you work.	1	463
464	Turn the next reflection into one concrete change, Reflection that helps you reset and prioritise is doing real work, and it does the most when it ends in a change. After your next reflection, do not just feel clearer. Name one specific thing you will do differently because of it, and start it. Reflection that produces a change compounds. Reflection that only produces a calmer mind resets you to the same place.	1	464
465	Write the direction down so it survives the week, Taking stock gives you clarity, and clarity is the first thing a busy week erodes. Make it durable. When a moment of reflection gives you a clear direction, write it in one or two plain lines and keep it where you will see it. The note holds the direction steady when the daily noise would otherwise scatter it within a few days.	1	465
466	Shrink reflection to one question at day's end, Reflection loses to a busy week because it sounds like a session you have to find time for. Shrink it until it fits the time you already have. Before you close your laptop, answer one question: what is the most useful thing I learned today? Sixty seconds, no notebook. Done daily, that is more genuine reflection than most people manage in a quiet month.	1	466
467	Attach reflection to something already in your day, Waiting for reflection to be built into how the team works means waiting indefinitely. You do not need the routine to change, you need to attach to it. Pick something that already happens every day, the commute, the first coffee, the end of a call, and make that your reflection cue: one question, same moment, every day. The trigger already exists. Give it a second job.	1	467
468	Run a two-minute review at the end of one task, Missed learning is the quiet cost of finishing a task and moving straight to the next. Insert a small pause. At the end of your next significant task, before moving on, spend two minutes on two questions: what worked here, and what would I do differently. That short review is where the learning gets captured. Without it, the experience happens and teaches you nothing.	1	468
469	Keep the habit that makes conversations two-way, Conversations that feel clear and genuinely two-way are produced by specific habits: asking before answering, checking understanding, leaving room for the other side. Notice which of those you do and keep doing it deliberately. Good conversations are not luck, and the ones around you stay good partly because of your part in them. Name your contribution so you can protect it.	1	469
470	Give one person the listening you value, Being genuinely listened to is rare enough that you notice it. You know how it feels, so pass it on. In your next conversation, give one person your full attention: no interrupting, no planning your reply while they talk, a clear acknowledgement of their point before you add yours. The experience of being understood spreads when people copy it, and you can start the copy.	1	470
471	Turn good communication into one acted-on thing, Communication that keeps you connected and aligned is doing its job when it changes what you do, not just what you know. After the next useful exchange, name one concrete thing you will do differently because of it. Communication that produces an action keeps you genuinely aligned. Communication you simply receive and file leaves you informed but not actually moved.	1	471
472	Reply with your read and ask them to confirm, When a message is unclear, the open question can you clarify often just returns more vagueness. State your best understanding instead: I am reading this as X, due Friday, is that right? Confirming a specific interpretation is faster for the other person than re-explaining from scratch, and you walk away with a precise answer and a written record of it.	1	472
473	Lead with your point and pick the right moment, Not feeling heard is often about timing and framing, both of which you control. Next time, open with your point in one clear sentence rather than building toward it, and choose a moment when the person can actually attend, not the end of a packed meeting. The same perspective, stated sharply at a moment someone can receive it, gets heard very differently.	1	473
474	Close conversations by confirming what was agreed, Most avoidable friction comes from people leaving a conversation with different versions of what was decided. Catch it at the exit. End your next few important conversations by briefly stating what you both agreed and what happens next, and checking it matches. Ten seconds of confirmation prevents the misalignment that turns into rework, blame, and friction a week later.	1	474
475	Name what you are learning from the stretch, Being stretched in a healthy way is producing growth, and growth is easy to miss while you are inside it. Make it visible to yourself. Every week or so, name one specific capability the stretch is building. Naming it does two things: it shows you the progress on the hard days, and it gives you something concrete to point to when you want the next opportunity.	1	475
476	Act on one piece of guidance within the week, Guidance that helps you improve only improves anything once you use it. Close the gap fast. Take the most useful recent piece of guidance and apply it to a real task this week, then tell the person who gave it what changed. Acting quickly cements the improvement and shows the person their guidance lands, which makes them more likely to keep investing it in you.	1	476
477	Write down what you can now do that you could not, Feeling your skills move forward is motivating, and it fades because each new ability quietly becomes the normal you forget you ever lacked. Capture the movement. Spend two minutes writing what you can do now that you could not three months ago. On the flat days, that list is concrete proof you are progressing, not just a feeling that comes and goes.	1	477
478	Add a short review so challenges become learning, A hard challenge does not teach you anything on its own. The learning comes from the review afterward, and skipping that is why challenges stop translating. Add the step. When you finish a demanding task, spend five minutes on what it taught you: what worked, what you would change. The challenge supplied the experience. The review is what converts it into a skill.	1	478
479	Ask one specific person for one specific thing, Feedback and support for growth rarely arrive unprompted, because nobody knows exactly what you need. Ask precisely. Pick one person and one specific thing: would you watch how I handle X and tell me one thing to improve? A narrow request aimed at the right person is easy to say yes to, and it gets you the targeted input that waiting never delivers.	1	479
480	Pick one skill and make one move on it this week, Feeling stuck usually means development has become passive, something you are waiting to receive. Make it active again with one small move. Choose a single skill you want to build and do one concrete thing toward it this week: a task that uses it, a person to learn from, twenty minutes of practice. Stuck breaks the moment you take one deliberate step yourself.	1	480
481	Tell your manager which work uses you best, Being trusted with work that uses your strengths is valuable and not guaranteed to continue, because assignments are made on rough guesses. Protect the fit. Tell your manager plainly which work plays to your strengths and why it goes well. Naming it makes the next assignment more likely to land in the same place rather than drifting somewhere that wastes what you do best.	1	481
482	Take one idea from thought to a small test, Having space to explore your ideas is only worth something if ideas actually move through it. Pick your most promising current idea and take it one concrete step: a small test, a quick prototype, a conversation with someone who could back it. An idea that gets tested becomes real or gets ruled out cleanly. An idea that only gets explored in your head stays a maybe.	1	482
483	Pick one growth option and take a first step, Sensing there are options to grow is good, and options only matter once you move on one. Choose the option that fits you best and take a single concrete step this week toward it: register interest, build a relevant skill, talk to someone who has done it. Options you act on open up. Options you only keep in mind tend to quietly stay options.	1	483
484	Ask for one task that uses your strongest skill, Strengths feel underused when the people assigning work do not have a clear picture of what you are best at. Give them one. Name your strongest skill to your manager and ask directly for one piece of work that would use it. A specific request tied to a specific strength is easy to act on. Hoping the right work finds you usually means it does not.	1	484
485	Bring the idea with a small first step attached, Ideas lose traction when they arrive as concepts that someone else has to figure out how to start. Remove that barrier. Next time you propose one, bring a small, concrete first step already worked out: here is the idea, and here is the low-cost thing we could do this week to test it. An idea with an easy first move is far more likely to actually begin.	1	485
486	Ask one person what the next step really takes, Opportunities to progress often feel limited because the path is unmapped, not because it is closed. Map one step of it. Find someone who has moved up from where you are and ask plainly what the step actually required: which skills, what experience, how they made the case. A path with a visible first move is something you can act on. An unmapped one only feels like a wall.	1	486
487	Use the safety on something that actually matters, Feeling safe to be honest is only worth something if you spend it on real things. This week, use it once: raise the genuine concern, give the honest feedback, admit the actual gap. Safety that only ever carries small, comfortable truths slowly stops being tested, and untested safety quietly weakens. Using it on something that matters is what keeps it real.	1	487
488	Respond with respect when someone else speaks up, Being met with respect when you speak up is what makes speaking up feel possible, and the same is true for everyone. Pass it on. Next time a colleague says something difficult or admits a problem, respond the way you would want to be met: take it seriously, with no hint of dismissal. Each respectful response makes the next person more willing to be honest.	1	488
489	Be the supportive response when someone is open, Openness being met with support is not a fixed feature of a place. It is made of how individuals react, one moment at a time. Be one of the people who makes it real. When someone is open with you about a struggle or a mistake, lead with support: what do you need, how can I help. Judgement and support are both contagious. You get to choose which one you spread.	1	489
490	Start with a low-stakes truth to test the water, Worry about consequences makes every honest thing feel like a big risk. Test the water with a small one. Pick something low-stakes and say it honestly, framed as an observation rather than a complaint, then watch how it lands. A few small, safe attempts give you real evidence about what happens here. Often the fear is larger than the reality, and only evidence shows you that.	1	490
491	Find one person you can be honest with first, When honesty feels risky across the board, you do not have to be honest with everyone to break the silence. Start with one. Identify a single colleague or manager who has earned some trust and have one honest conversation with them. A safe environment is built from individual safe relationships. One reliable person is a real foothold, and it is more than the all-or-nothing version offers.	1	491
492	Share one thing you would normally keep back, Holding back protects you in the short term and quietly costs you in trust, because people sense the distance. Close a little of it on purpose. Pick one thing you would normally keep to yourself, a view, a concern, a small uncertainty, and share it with the right person this week. Trust grows from small, deliberate disclosures. Holding everything back guarantees it stays where it is.	1	492
493	Notice the prioritising habit that keeps it manageable, A workload that feels manageable is usually the result of a habit working well: a weekly plan, a clear ranking, a regular tidy of the list. Notice which habit is doing it for you and keep it deliberate. Workloads tip over quietly when the habit slips. Knowing exactly what is keeping yours steady means you can protect it when a busy week tries to crowd it out.	1	493
494	Protect whatever is giving you the focus time, Focus time that lets you make meaningful progress is the first thing a busy period erodes. Notice what is creating it: a blocked morning, a habit of closing channels, a meeting you keep clear. Then defend that specific condition deliberately next week, before the calendar fills. Focus time you actively protect survives. Focus time you assume will be there gets taken.	1	494
495	Use the calm to get ahead on something important, Being able to plan without constant disruption is a good window, and good windows are best spent getting ahead, not just keeping pace. Use the current calm to move one important but non-urgent thing forward. Disruption always returns. Work you pulled forward during a settled stretch is the buffer that keeps the next busy period from knocking you off course.	1	495
496	Get one priority confirmed as the genuine top, Competing priorities make focus impossible because you are quietly choosing between them all day, under pressure, alone. Take the choice out of your head. List them, take the list to whoever sets the work, and ask one question: if only one moves this week, which is it? A single confirmed top priority does not clear the list, but it tells you where to actually put your focus.	1	496
497	Show the full load before you take on more, A heavy, unpredictable workload stays that way because each new task is added on its own, with nobody seeing the whole. Make the whole visible. Next time you are asked to take something on, do not just say yes. Show what is already on your plate and ask what should move to make room. The trade-off was always there. You are making it a decision someone has to see.	1	497
498	Protect one block a day from interruption, Interruptions reduce productivity more than their length suggests, because each one costs the time to refocus afterward. Carve out one defended block. Pick one period each day, mark it as unavailable, close the channels, and use it for work that needs real concentration. You will not stop every interruption, but one protected block daily is where the meaningful work actually gets done.	1	498
499	Sharpen the question your reflection asks, A reflection habit you already keep is worth improving rather than just continuing. Most reflection asks how did it go, which is too broad to produce much. Swap it for something sharper: what did I do that worked and should repeat, and what cost me more than it should have. A better question is what turns a steady habit into one that actually changes how you work.	1	499
500	Turn the next reflection into one concrete change, Reflection that helps you reset and prioritise is doing real work, and it does the most when it ends in a change. After your next reflection, do not just feel clearer. Name one specific thing you will do differently because of it, and start it. Reflection that produces a change compounds. Reflection that only produces a calmer mind resets you to the same place.	1	500
501	Write the direction down so it survives the week, Taking stock gives you clarity, and clarity is the first thing a busy week erodes. Make it durable. When a moment of reflection gives you a clear direction, write it in one or two plain lines and keep it where you will see it. The note holds the direction steady when the daily noise would otherwise scatter it within a few days.	1	501
502	Shrink reflection to one question at day's end, Reflection loses to a busy week because it sounds like a session you have to find time for. Shrink it until it fits the time you already have. Before you close your laptop, answer one question: what is the most useful thing I learned today? Sixty seconds, no notebook. Done daily, that is more genuine reflection than most people manage in a quiet month.	1	502
503	Attach reflection to something already in your day, Waiting for reflection to be built into how the team works means waiting indefinitely. You do not need the routine to change, you need to attach to it. Pick something that already happens every day, the commute, the first coffee, the end of a call, and make that your reflection cue: one question, same moment, every day. The trigger already exists. Give it a second job.	1	503
504	Run a two-minute review at the end of one task, Missed learning is the quiet cost of finishing a task and moving straight to the next. Insert a small pause. At the end of your next significant task, before moving on, spend two minutes on two questions: what worked here, and what would I do differently. That short review is where the learning gets captured. Without it, the experience happens and teaches you nothing.	1	504
505	Keep the habit that makes conversations two-way, Conversations that feel clear and genuinely two-way are produced by specific habits: asking before answering, checking understanding, leaving room for the other side. Notice which of those you do and keep doing it deliberately. Good conversations are not luck, and the ones around you stay good partly because of your part in them. Name your contribution so you can protect it.	1	505
506	Give one person the listening you value, Being genuinely listened to is rare enough that you notice it. You know how it feels, so pass it on. In your next conversation, give one person your full attention: no interrupting, no planning your reply while they talk, a clear acknowledgement of their point before you add yours. The experience of being understood spreads when people copy it, and you can start the copy.	1	506
507	Turn good communication into one acted-on thing, Communication that keeps you connected and aligned is doing its job when it changes what you do, not just what you know. After the next useful exchange, name one concrete thing you will do differently because of it. Communication that produces an action keeps you genuinely aligned. Communication you simply receive and file leaves you informed but not actually moved.	1	507
508	Reply with your read and ask them to confirm, When a message is unclear, the open question can you clarify often just returns more vagueness. State your best understanding instead: I am reading this as X, due Friday, is that right? Confirming a specific interpretation is faster for the other person than re-explaining from scratch, and you walk away with a precise answer and a written record of it.	1	508
509	Lead with your point and pick the right moment, Not feeling heard is often about timing and framing, both of which you control. Next time, open with your point in one clear sentence rather than building toward it, and choose a moment when the person can actually attend, not the end of a packed meeting. The same perspective, stated sharply at a moment someone can receive it, gets heard very differently.	1	509
510	Close conversations by confirming what was agreed, Most avoidable friction comes from people leaving a conversation with different versions of what was decided. Catch it at the exit. End your next few important conversations by briefly stating what you both agreed and what happens next, and checking it matches. Ten seconds of confirmation prevents the misalignment that turns into rework, blame, and friction a week later.	1	510
511	Write down the new knowledge so it does not fade, Knowledge picked up through work is the easiest kind to lose, because it never felt like studying. Pin it down. Take the most useful thing you have learned recently and write it as one or two plain lines somewhere you will find it again. The note is what separates something you genuinely know from something you happened to encounter and will half-remember in a month.	1	511
512	Use the next practice chance on your weakest part, Chances to practice are most valuable when you point them at what you are not yet good at, not what is already comfortable. Next time you get one, deliberately use it on the part you find hardest. Practising the easy bits feels productive and changes little. Practising the weak bit is mildly uncomfortable and is where the actual skill is built.	1	512
513	Tell your manager which learning is paying off, Learning that feels relevant to your role is more fragile than it looks, because what you get offered is set by other people's guesses. Protect the fit. Tell your manager which recent learning has actually been useful and why. Naming what is landing makes it far more likely the next opportunity is pointed the same way rather than drifting off target.	1	513
514	Make space by learning inside a current task, When there is no obvious space to learn something new, stop looking for a separate space and fold it into the work. Pick one current task and do a part of it in a way that requires learning something: a new method, a tool feature, a technique you have read about but never used. The space does not have to be found. It can be made inside what you already do.	1	514
515	Change one thing about a repetitive task this week, Repetitive work stops offering development when you run it the same way every time, but the repetition is not the only fixed thing. Pick one repetitive task and change a single element: do it faster, do it cleaner, automate a step, improve the output. The development moment is in the change you introduce, not in waiting for the task itself to become different.	1	515
516	Protect twenty minutes before the demands start, Learning gets squeezed out because it has no deadline and everything else does, so it always loses the moment the day starts. Move it before the day starts. Block twenty minutes early, before the demands arrive, for one specific piece of learning. Time taken at the front of the day is yours. Time you hope to find later has already been promised to something louder.	1	516
517	Tell your manager which work uses you best, Being trusted with work that uses your strengths is valuable and not guaranteed to continue, because assignments are made on rough guesses. Protect the fit. Tell your manager plainly which work plays to your strengths and why it goes well. Naming it makes the next assignment more likely to land in the same place rather than drifting somewhere that wastes what you do best.	1	517
518	Take one idea from thought to a small test, Having space to explore your ideas is only worth something if ideas actually move through it. Pick your most promising current idea and take it one concrete step: a small test, a quick prototype, a conversation with someone who could back it. An idea that gets tested becomes real or gets ruled out cleanly. An idea that only gets explored in your head stays a maybe.	1	518
519	Pick one growth option and take a first step, Sensing there are options to grow is good, and options only matter once you move on one. Choose the option that fits you best and take a single concrete step this week toward it: register interest, build a relevant skill, talk to someone who has done it. Options you act on open up. Options you only keep in mind tend to quietly stay options.	1	519
520	Ask for one task that uses your strongest skill, Strengths feel underused when the people assigning work do not have a clear picture of what you are best at. Give them one. Name your strongest skill to your manager and ask directly for one piece of work that would use it. A specific request tied to a specific strength is easy to act on. Hoping the right work finds you usually means it does not.	1	520
521	Bring the idea with a small first step attached, Ideas lose traction when they arrive as concepts that someone else has to figure out how to start. Remove that barrier. Next time you propose one, bring a small, concrete first step already worked out: here is the idea, and here is the low-cost thing we could do this week to test it. An idea with an easy first move is far more likely to actually begin.	1	521
522	Ask one person what the next step really takes, Opportunities to progress often feel limited because the path is unmapped, not because it is closed. Map one step of it. Find someone who has moved up from where you are and ask plainly what the step actually required: which skills, what experience, how they made the case. A path with a visible first move is something you can act on. An unmapped one only feels like a wall.	1	522
523	Raise the next concern early and with a suggestion, Being taken seriously when you raise concerns is worth using well. Raise the next one early, while it is still small, and bring a suggested fix alongside it. A concern raised early with an idea attached is easy to act on and confirms you as someone worth listening to. Concerns saved up until they are urgent are harder to address and harder to hear.	1	523
524	Share the lesson from your next mistake out loud, Mistakes being treated as learning only stays true while people actually share what they learn. Keep that real. Next time you get something wrong, name it openly and say what it taught you, where the team can hear. One person turning a mistake into a visible lesson keeps the practice alive and gives everyone else permission to do the same.	1	524
525	Use your candour to make space for someone quieter, Being comfortable with candour is a genuine asset, and you can use it for more than your own points. Use it to draw others out. When you are in a conversation, notice who is holding back and invite them in directly: what is your read on this? Your ease with speaking openly can create room for people who do not yet have it.	1	525
526	Raise the concern with its cost attached, Concerns get dismissed easily when they sound like opinions. Attach a cost and they become harder to wave off. Next time you raise one, include the concrete consequence: if this is not addressed, here is what it will cost in time, rework, or risk. A concern with a specific price tag is a business problem someone has to weigh, not just a worry they can set aside.	1	526
527	Frame the issue around the problem rather than the person, Blame and defensiveness flare when an issue sounds like an accusation. You can lower the temperature by how you raise it. Talk about the problem and the situation, not who caused it: here is what is happening, here is what I think we should do. Keeping the focus on the problem rather than a person gives everyone room to fix it instead of defend themselves.	1	527
528	Be candid once on something low-stakes, Avoiding candour protects you in the moment and slowly costs you, because problems you do not name do not get solved. Loosen it gradually. Pick one low-stakes thing and be honest about it this week, framed constructively. A small, safe act of candour gives you evidence about how honesty is actually received here, which is more useful than the caution that has kept you quiet.	1	528
529	Notice the prioritising habit that keeps it manageable, A workload that feels manageable is usually the result of a habit working well: a weekly plan, a clear ranking, a regular tidy of the list. Notice which habit is doing it for you and keep it deliberate. Workloads tip over quietly when the habit slips. Knowing exactly what is keeping yours steady means you can protect it when a busy week tries to crowd it out.	1	529
530	Protect whatever is giving you the focus time, Focus time that lets you make meaningful progress is the first thing a busy period erodes. Notice what is creating it: a blocked morning, a habit of closing channels, a meeting you keep clear. Then defend that specific condition deliberately next week, before the calendar fills. Focus time you actively protect survives. Focus time you assume will be there gets taken.	1	530
531	Use the calm to get ahead on something important, Being able to plan without constant disruption is a good window, and good windows are best spent getting ahead, not just keeping pace. Use the current calm to move one important but non-urgent thing forward. Disruption always returns. Work you pulled forward during a settled stretch is the buffer that keeps the next busy period from knocking you off course.	1	531
532	Get one priority confirmed as the genuine top, Competing priorities make focus impossible because you are quietly choosing between them all day, under pressure, alone. Take the choice out of your head. List them, take the list to whoever sets the work, and ask one question: if only one moves this week, which is it? A single confirmed top priority does not clear the list, but it tells you where to actually put your focus.	1	532
533	Show the full load before you take on more, A heavy, unpredictable workload stays that way because each new task is added on its own, with nobody seeing the whole. Make the whole visible. Next time you are asked to take something on, do not just say yes. Show what is already on your plate and ask what should move to make room. The trade-off was always there. You are making it a decision someone has to see.	1	533
534	Protect one block a day from interruption, Interruptions reduce productivity more than their length suggests, because each one costs the time to refocus afterward. Carve out one defended block. Pick one period each day, mark it as unavailable, close the channels, and use it for work that needs real concentration. You will not stop every interruption, but one protected block daily is where the meaningful work actually gets done.	1	534
535	Sharpen the question your reflection asks, A reflection habit you already keep is worth improving rather than just continuing. Most reflection asks how did it go, which is too broad to produce much. Swap it for something sharper: what did I do that worked and should repeat, and what cost me more than it should have. A better question is what turns a steady habit into one that actually changes how you work.	1	535
536	Turn the next reflection into one concrete change, Reflection that helps you reset and prioritise is doing real work, and it does the most when it ends in a change. After your next reflection, do not just feel clearer. Name one specific thing you will do differently because of it, and start it. Reflection that produces a change compounds. Reflection that only produces a calmer mind resets you to the same place.	1	536
537	Write the direction down so it survives the week, Taking stock gives you clarity, and clarity is the first thing a busy week erodes. Make it durable. When a moment of reflection gives you a clear direction, write it in one or two plain lines and keep it where you will see it. The note holds the direction steady when the daily noise would otherwise scatter it within a few days.	1	537
538	Shrink reflection to one question at day's end, Reflection loses to a busy week because it sounds like a session you have to find time for. Shrink it until it fits the time you already have. Before you close your laptop, answer one question: what is the most useful thing I learned today? Sixty seconds, no notebook. Done daily, that is more genuine reflection than most people manage in a quiet month.	1	538
539	Attach reflection to something already in your day, Waiting for reflection to be built into how the team works means waiting indefinitely. You do not need the routine to change, you need to attach to it. Pick something that already happens every day, the commute, the first coffee, the end of a call, and make that your reflection cue: one question, same moment, every day. The trigger already exists. Give it a second job.	1	539
540	Run a two-minute review at the end of one task, Missed learning is the quiet cost of finishing a task and moving straight to the next. Insert a small pause. At the end of your next significant task, before moving on, spend two minutes on two questions: what worked here, and what would I do differently. That short review is where the learning gets captured. Without it, the experience happens and teaches you nothing.	1	540
541	Keep the habit that makes conversations two-way, Conversations that feel clear and genuinely two-way are produced by specific habits: asking before answering, checking understanding, leaving room for the other side. Notice which of those you do and keep doing it deliberately. Good conversations are not luck, and the ones around you stay good partly because of your part in them. Name your contribution so you can protect it.	1	541
542	Give one person the listening you value, Being genuinely listened to is rare enough that you notice it. You know how it feels, so pass it on. In your next conversation, give one person your full attention: no interrupting, no planning your reply while they talk, a clear acknowledgement of their point before you add yours. The experience of being understood spreads when people copy it, and you can start the copy.	1	542
543	Turn good communication into one acted-on thing, Communication that keeps you connected and aligned is doing its job when it changes what you do, not just what you know. After the next useful exchange, name one concrete thing you will do differently because of it. Communication that produces an action keeps you genuinely aligned. Communication you simply receive and file leaves you informed but not actually moved.	1	543
544	Reply with your read and ask them to confirm, When a message is unclear, the open question can you clarify often just returns more vagueness. State your best understanding instead: I am reading this as X, due Friday, is that right? Confirming a specific interpretation is faster for the other person than re-explaining from scratch, and you walk away with a precise answer and a written record of it.	1	544
545	Lead with your point and pick the right moment, Not feeling heard is often about timing and framing, both of which you control. Next time, open with your point in one clear sentence rather than building toward it, and choose a moment when the person can actually attend, not the end of a packed meeting. The same perspective, stated sharply at a moment someone can receive it, gets heard very differently.	1	545
546	Close conversations by confirming what was agreed, Most avoidable friction comes from people leaving a conversation with different versions of what was decided. Catch it at the exit. End your next few important conversations by briefly stating what you both agreed and what happens next, and checking it matches. Ten seconds of confirmation prevents the misalignment that turns into rework, blame, and friction a week later.	1	546
547	Write down the new knowledge so it does not fade, Knowledge picked up through work is the easiest kind to lose, because it never felt like studying. Pin it down. Take the most useful thing you have learned recently and write it as one or two plain lines somewhere you will find it again. The note is what separates something you genuinely know from something you happened to encounter and will half-remember in a month.	1	547
548	Use the next practice chance on your weakest part, Chances to practice are most valuable when you point them at what you are not yet good at, not what is already comfortable. Next time you get one, deliberately use it on the part you find hardest. Practising the easy bits feels productive and changes little. Practising the weak bit is mildly uncomfortable and is where the actual skill is built.	1	548
549	Tell your manager which learning is paying off, Learning that feels relevant to your role is more fragile than it looks, because what you get offered is set by other people's guesses. Protect the fit. Tell your manager which recent learning has actually been useful and why. Naming what is landing makes it far more likely the next opportunity is pointed the same way rather than drifting off target.	1	549
550	Make space by learning inside a current task, When there is no obvious space to learn something new, stop looking for a separate space and fold it into the work. Pick one current task and do a part of it in a way that requires learning something: a new method, a tool feature, a technique you have read about but never used. The space does not have to be found. It can be made inside what you already do.	1	550
551	Change one thing about a repetitive task this week, Repetitive work stops offering development when you run it the same way every time, but the repetition is not the only fixed thing. Pick one repetitive task and change a single element: do it faster, do it cleaner, automate a step, improve the output. The development moment is in the change you introduce, not in waiting for the task itself to become different.	1	551
552	Protect twenty minutes before the demands start, Learning gets squeezed out because it has no deadline and everything else does, so it always loses the moment the day starts. Move it before the day starts. Block twenty minutes early, before the demands arrive, for one specific piece of learning. Time taken at the front of the day is yours. Time you hope to find later has already been promised to something louder.	1	552
553	Tell your manager which work uses you best, Being trusted with work that uses your strengths is valuable and not guaranteed to continue, because assignments are made on rough guesses. Protect the fit. Tell your manager plainly which work plays to your strengths and why it goes well. Naming it makes the next assignment more likely to land in the same place rather than drifting somewhere that wastes what you do best.	1	553
554	Take one idea from thought to a small test, Having space to explore your ideas is only worth something if ideas actually move through it. Pick your most promising current idea and take it one concrete step: a small test, a quick prototype, a conversation with someone who could back it. An idea that gets tested becomes real or gets ruled out cleanly. An idea that only gets explored in your head stays a maybe.	1	554
555	Pick one growth option and take a first step, Sensing there are options to grow is good, and options only matter once you move on one. Choose the option that fits you best and take a single concrete step this week toward it: register interest, build a relevant skill, talk to someone who has done it. Options you act on open up. Options you only keep in mind tend to quietly stay options.	1	555
556	Ask for one task that uses your strongest skill, Strengths feel underused when the people assigning work do not have a clear picture of what you are best at. Give them one. Name your strongest skill to your manager and ask directly for one piece of work that would use it. A specific request tied to a specific strength is easy to act on. Hoping the right work finds you usually means it does not.	1	556
557	Bring the idea with a small first step attached, Ideas lose traction when they arrive as concepts that someone else has to figure out how to start. Remove that barrier. Next time you propose one, bring a small, concrete first step already worked out: here is the idea, and here is the low-cost thing we could do this week to test it. An idea with an easy first move is far more likely to actually begin.	1	557
558	Ask one person what the next step really takes, Opportunities to progress often feel limited because the path is unmapped, not because it is closed. Map one step of it. Find someone who has moved up from where you are and ask plainly what the step actually required: which skills, what experience, how they made the case. A path with a visible first move is something you can act on. An unmapped one only feels like a wall.	1	558
559	Raise the next concern early and with a suggestion, Being taken seriously when you raise concerns is worth using well. Raise the next one early, while it is still small, and bring a suggested fix alongside it. A concern raised early with an idea attached is easy to act on and confirms you as someone worth listening to. Concerns saved up until they are urgent are harder to address and harder to hear.	1	559
560	Share the lesson from your next mistake out loud, Mistakes being treated as learning only stays true while people actually share what they learn. Keep that real. Next time you get something wrong, name it openly and say what it taught you, where the team can hear. One person turning a mistake into a visible lesson keeps the practice alive and gives everyone else permission to do the same.	1	560
561	Use your candour to make space for someone quieter, Being comfortable with candour is a genuine asset, and you can use it for more than your own points. Use it to draw others out. When you are in a conversation, notice who is holding back and invite them in directly: what is your read on this? Your ease with speaking openly can create room for people who do not yet have it.	1	561
562	Raise the concern with its cost attached, Concerns get dismissed easily when they sound like opinions. Attach a cost and they become harder to wave off. Next time you raise one, include the concrete consequence: if this is not addressed, here is what it will cost in time, rework, or risk. A concern with a specific price tag is a business problem someone has to weigh, not just a worry they can set aside.	1	562
563	Frame the issue around the problem rather than the person, Blame and defensiveness flare when an issue sounds like an accusation. You can lower the temperature by how you raise it. Talk about the problem and the situation, not who caused it: here is what is happening, here is what I think we should do. Keeping the focus on the problem rather than a person gives everyone room to fix it instead of defend themselves.	1	563
564	Be candid once on something low-stakes, Avoiding candour protects you in the moment and slowly costs you, because problems you do not name do not get solved. Loosen it gradually. Pick one low-stakes thing and be honest about it this week, framed constructively. A small, safe act of candour gives you evidence about how honesty is actually received here, which is more useful than the caution that has kept you quiet.	1	564
565	Learn one more thing your reliable tools can do, Reliable tools usually do more than you currently use, and that unused capability is free productivity. Spend fifteen minutes on one tool you depend on, learning a feature you have never tried: a shortcut, an automation, a better view. Most people use a fraction of what their tools offer. Closing a little of that gap pays back every day after.	1	565
566	Show a colleague the system trick that helps you, If a system makes your work efficient, the same trick will help your colleagues, and most of them have not found it. Spend five minutes showing one person the specific thing that speeds you up. Your private efficiency becomes a shared one, and the colleague will usually trade you a tip you did not know. Good practice spreads person to person.	1	566
567	Know which tool earns its place and why, When technology genuinely supports your quality and speed, it is worth knowing which specific tools are doing the heavy lifting and how. Tooling decisions come around regularly, and they are often made by people who do not use the tools daily. Being able to say clearly what works and why means you can argue to keep it. People defend what they can describe.	1	567
568	Report the fault with steps and frequency, A tool problem described vaguely, it is slow, it is annoying, is easy for IT or support to deprioritise. Make it actionable. Write down exactly what you do, exactly what goes wrong, and how often it costs you time. A precise, repeatable report with a real cost attached is something someone can fix and justify fixing. A general complaint just waits in a queue.	1	568
569	Build one small bridge for the worst handoff, When systems do not integrate, the time disappears at the handoff: re-keying, copying, double-checking. You cannot fix the integration, but you can make the worst handoff reliable. Build a small bridge for it, a checklist or a template that makes the manual step quick and error-free. It will not solve the gap, but it stops the gap from costing you twice every time.	1	569
570	Turn your best workaround into a shared shortcut, If you are losing time to tool limitations, you have probably built workarounds already, scattered and informal. Consolidate the best one. Take the workaround that saves you the most and make it solid: write it down, turn it into a template or a saved process. A messy workaround done repeatedly stays a cost. A clean one becomes a shortcut you and others can reuse.	1	570
571	Sharpen the question your reflection asks, A reflection habit you already keep is worth improving rather than just continuing. Most reflection asks how did it go, which is too broad to produce much. Swap it for something sharper: what did I do that worked and should repeat, and what cost me more than it should have. A better question is what turns a steady habit into one that actually changes how you work.	1	571
572	Turn the next reflection into one concrete change, Reflection that helps you reset and prioritise is doing real work, and it does the most when it ends in a change. After your next reflection, do not just feel clearer. Name one specific thing you will do differently because of it, and start it. Reflection that produces a change compounds. Reflection that only produces a calmer mind resets you to the same place.	1	572
573	Write the direction down so it survives the week, Taking stock gives you clarity, and clarity is the first thing a busy week erodes. Make it durable. When a moment of reflection gives you a clear direction, write it in one or two plain lines and keep it where you will see it. The note holds the direction steady when the daily noise would otherwise scatter it within a few days.	1	573
574	Shrink reflection to one question at day's end, Reflection loses to a busy week because it sounds like a session you have to find time for. Shrink it until it fits the time you already have. Before you close your laptop, answer one question: what is the most useful thing I learned today? Sixty seconds, no notebook. Done daily, that is more genuine reflection than most people manage in a quiet month.	1	574
575	Attach reflection to something already in your day, Waiting for reflection to be built into how the team works means waiting indefinitely. You do not need the routine to change, you need to attach to it. Pick something that already happens every day, the commute, the first coffee, the end of a call, and make that your reflection cue: one question, same moment, every day. The trigger already exists. Give it a second job.	1	575
576	Run a two-minute review at the end of one task, Missed learning is the quiet cost of finishing a task and moving straight to the next. Insert a small pause. At the end of your next significant task, before moving on, spend two minutes on two questions: what worked here, and what would I do differently. That short review is where the learning gets captured. Without it, the experience happens and teaches you nothing.	1	576
577	Keep the habit that makes conversations two-way, Conversations that feel clear and genuinely two-way are produced by specific habits: asking before answering, checking understanding, leaving room for the other side. Notice which of those you do and keep doing it deliberately. Good conversations are not luck, and the ones around you stay good partly because of your part in them. Name your contribution so you can protect it.	1	577
578	Give one person the listening you value, Being genuinely listened to is rare enough that you notice it. You know how it feels, so pass it on. In your next conversation, give one person your full attention: no interrupting, no planning your reply while they talk, a clear acknowledgement of their point before you add yours. The experience of being understood spreads when people copy it, and you can start the copy.	1	578
579	Turn good communication into one acted-on thing, Communication that keeps you connected and aligned is doing its job when it changes what you do, not just what you know. After the next useful exchange, name one concrete thing you will do differently because of it. Communication that produces an action keeps you genuinely aligned. Communication you simply receive and file leaves you informed but not actually moved.	1	579
580	Reply with your read and ask them to confirm, When a message is unclear, the open question can you clarify often just returns more vagueness. State your best understanding instead: I am reading this as X, due Friday, is that right? Confirming a specific interpretation is faster for the other person than re-explaining from scratch, and you walk away with a precise answer and a written record of it.	1	580
581	Lead with your point and pick the right moment, Not feeling heard is often about timing and framing, both of which you control. Next time, open with your point in one clear sentence rather than building toward it, and choose a moment when the person can actually attend, not the end of a packed meeting. The same perspective, stated sharply at a moment someone can receive it, gets heard very differently.	1	581
582	Close conversations by confirming what was agreed, Most avoidable friction comes from people leaving a conversation with different versions of what was decided. Catch it at the exit. End your next few important conversations by briefly stating what you both agreed and what happens next, and checking it matches. Ten seconds of confirmation prevents the misalignment that turns into rework, blame, and friction a week later.	1	582
583	Write down the new knowledge so it does not fade, Knowledge picked up through work is the easiest kind to lose, because it never felt like studying. Pin it down. Take the most useful thing you have learned recently and write it as one or two plain lines somewhere you will find it again. The note is what separates something you genuinely know from something you happened to encounter and will half-remember in a month.	1	583
584	Use the next practice chance on your weakest part, Chances to practice are most valuable when you point them at what you are not yet good at, not what is already comfortable. Next time you get one, deliberately use it on the part you find hardest. Practising the easy bits feels productive and changes little. Practising the weak bit is mildly uncomfortable and is where the actual skill is built.	1	584
585	Tell your manager which learning is paying off, Learning that feels relevant to your role is more fragile than it looks, because what you get offered is set by other people's guesses. Protect the fit. Tell your manager which recent learning has actually been useful and why. Naming what is landing makes it far more likely the next opportunity is pointed the same way rather than drifting off target.	1	585
586	Make space by learning inside a current task, When there is no obvious space to learn something new, stop looking for a separate space and fold it into the work. Pick one current task and do a part of it in a way that requires learning something: a new method, a tool feature, a technique you have read about but never used. The space does not have to be found. It can be made inside what you already do.	1	586
587	Change one thing about a repetitive task this week, Repetitive work stops offering development when you run it the same way every time, but the repetition is not the only fixed thing. Pick one repetitive task and change a single element: do it faster, do it cleaner, automate a step, improve the output. The development moment is in the change you introduce, not in waiting for the task itself to become different.	1	587
588	Protect twenty minutes before the demands start, Learning gets squeezed out because it has no deadline and everything else does, so it always loses the moment the day starts. Move it before the day starts. Block twenty minutes early, before the demands arrive, for one specific piece of learning. Time taken at the front of the day is yours. Time you hope to find later has already been promised to something louder.	1	588
589	Tell your manager which work uses you best, Being trusted with work that uses your strengths is valuable and not guaranteed to continue, because assignments are made on rough guesses. Protect the fit. Tell your manager plainly which work plays to your strengths and why it goes well. Naming it makes the next assignment more likely to land in the same place rather than drifting somewhere that wastes what you do best.	1	589
590	Take one idea from thought to a small test, Having space to explore your ideas is only worth something if ideas actually move through it. Pick your most promising current idea and take it one concrete step: a small test, a quick prototype, a conversation with someone who could back it. An idea that gets tested becomes real or gets ruled out cleanly. An idea that only gets explored in your head stays a maybe.	1	590
591	Pick one growth option and take a first step, Sensing there are options to grow is good, and options only matter once you move on one. Choose the option that fits you best and take a single concrete step this week toward it: register interest, build a relevant skill, talk to someone who has done it. Options you act on open up. Options you only keep in mind tend to quietly stay options.	1	591
592	Ask for one task that uses your strongest skill, Strengths feel underused when the people assigning work do not have a clear picture of what you are best at. Give them one. Name your strongest skill to your manager and ask directly for one piece of work that would use it. A specific request tied to a specific strength is easy to act on. Hoping the right work finds you usually means it does not.	1	592
593	Bring the idea with a small first step attached, Ideas lose traction when they arrive as concepts that someone else has to figure out how to start. Remove that barrier. Next time you propose one, bring a small, concrete first step already worked out: here is the idea, and here is the low-cost thing we could do this week to test it. An idea with an easy first move is far more likely to actually begin.	1	593
594	Ask one person what the next step really takes, Opportunities to progress often feel limited because the path is unmapped, not because it is closed. Map one step of it. Find someone who has moved up from where you are and ask plainly what the step actually required: which skills, what experience, how they made the case. A path with a visible first move is something you can act on. An unmapped one only feels like a wall.	1	594
595	Make space for someone who is not at ease yet, Feeling accepted and able to be yourself is something not everyone around you has, especially people who are newer or different from the group. Use your ease to widen the room. Make one deliberate move this week to help someone less settled feel they belong: include them, ask their view, make a connection. Acceptance grows when the people who have it extend it.	1	595
596	Show empathy first when a colleague is struggling, Empathy when challenges arise is built from individual responses, not policy, and you are one of the responders. Be deliberate about it. Next time a colleague is visibly having a hard time, lead with the person before the task: a genuine question, a real offer of help. Each time someone responds that way, it becomes a little more the normal here.	1	596
597	Use the trust on something genuinely difficult, Trust to share difficult things openly is only worth having if you use it for the genuinely difficult, not just the mildly awkward. This week, if there is something real you have been holding back, share it with the right person. Trust that only ever carries easy things slowly fades. Used on something that matters, it gets stronger and proves itself real.	1	597
598	Lower the guard once with one trusted person, Being guarded keeps you safe and also keeps you slightly apart, because people connect with what you let them see. You do not have to drop the guard everywhere. Pick one person who has earned some trust and share one slightly more honest thing than usual. Connection deepens through small, chosen disclosures. A guard that never lowers guarantees the distance stays.	1	598
599	Tell one person plainly what you need from them, When empathy and understanding feel absent, people are often not unkind, just unaware, because they cannot see what you have not shown them. Give one person something to respond to. Tell a colleague you trust, plainly, what is hard right now and what would help. Understanding usually follows knowing. People cannot meet a difficulty you have kept invisible.	1	599
600	Rebuild engagement from one safe interaction, When low acceptance has worn down your confidence, withdrawing further feels safer and quietly makes it worse. Rebuild in small steps. Pick one low-risk interaction this week, a conversation with someone safe, a small contribution in a friendly setting, and engage just there. Confidence comes back through repeated small, positive contact, not through waiting until you feel ready.	1	600
601	Notice what gives you clarity and keep asking for it, Having the clarity and support to perform well is not the default, so it is worth knowing what produces it. Notice the specifics: the brief that was clear, the question that was answered fast, the access you were given. Then keep asking for those same things by name on the next piece of work. Clarity you can describe is clarity you can request again.	1	601
602	Flag the next dependency before it becomes a blocker, Dependencies get handled efficiently partly because someone raises them early. Keep being that someone. Look ahead on your current work and name the next thing you will need from someone else, then flag it now rather than when you reach it. A dependency raised early is a scheduling note. The same dependency raised late is a blocker. The difference is who looks ahead.	1	602
603	Protect the conditions that keep the path clear, Steady progress without major blockers usually has causes worth protecting: good handoffs, early planning, the right access in place. Notice which conditions are keeping your path clear and treat them as things to maintain, not luck. A clear run tends to end when one of those quietly slips. Knowing what they are means you can catch the slip before it becomes a blockage.	1	603
604	Name the recurring blocker and tackle its cause, Blockers that keep recurring are usually one or two specific things, not bad luck. Find the pattern. For a week, note what actually stops you each time. One cause will likely repeat. Then tackle that single root: the approval that is always slow, the information always missing. Fixing one recurring blocker at its source clears more time than firefighting each instance as it lands.	1	604
605	Make one specific list of exactly what you need, Missing information, access, or support stays missing when the need is vague. Make it precise. Write one short, specific list: exactly what you need, from whom, by when, and why it is blocking you. Then send it to the people who can act. A clear, itemised request is hard to ignore and easy to fulfil. A general I do not have what I need is neither.	1	605
606	Look for the constraint that effort cannot fix, When strong effort still does not produce delivery, the problem is usually not effort, and adding more will not help. Look for the constraint. Step back and find the one thing genuinely in the way: a missing decision, a broken process, an unrealistic scope. Name it. A constraint you have identified can be raised and addressed. Working harder against it just costs more for the same result.	1	606
607	Sharpen the question your reflection asks, A reflection habit you already keep is worth improving rather than just continuing. Most reflection asks how did it go, which is too broad to produce much. Swap it for something sharper: what did I do that worked and should repeat, and what cost me more than it should have. A better question is what turns a steady habit into one that actually changes how you work.	1	607
608	Turn the next reflection into one concrete change, Reflection that helps you reset and prioritise is doing real work, and it does the most when it ends in a change. After your next reflection, do not just feel clearer. Name one specific thing you will do differently because of it, and start it. Reflection that produces a change compounds. Reflection that only produces a calmer mind resets you to the same place.	1	608
609	Write the direction down so it survives the week, Taking stock gives you clarity, and clarity is the first thing a busy week erodes. Make it durable. When a moment of reflection gives you a clear direction, write it in one or two plain lines and keep it where you will see it. The note holds the direction steady when the daily noise would otherwise scatter it within a few days.	1	609
610	Shrink reflection to one question at day's end, Reflection loses to a busy week because it sounds like a session you have to find time for. Shrink it until it fits the time you already have. Before you close your laptop, answer one question: what is the most useful thing I learned today? Sixty seconds, no notebook. Done daily, that is more genuine reflection than most people manage in a quiet month.	1	610
611	Attach reflection to something already in your day, Waiting for reflection to be built into how the team works means waiting indefinitely. You do not need the routine to change, you need to attach to it. Pick something that already happens every day, the commute, the first coffee, the end of a call, and make that your reflection cue: one question, same moment, every day. The trigger already exists. Give it a second job.	1	611
612	Run a two-minute review at the end of one task, Missed learning is the quiet cost of finishing a task and moving straight to the next. Insert a small pause. At the end of your next significant task, before moving on, spend two minutes on two questions: what worked here, and what would I do differently. That short review is where the learning gets captured. Without it, the experience happens and teaches you nothing.	1	612
613	Use the early notice to actually get ahead, Being told about changes early only helps if you use the head start, and it is easy to file the information and carry on as before. When you next get early notice of something, take fifteen minutes to do one concrete thing the change will require, before it lands. Early information is worth the most to the person who acts on it early.	1	613
614	Pass the reasoning on to someone who missed it, Understanding why a decision was made is an advantage, and not everyone hears the reasoning. When you next work with someone who seems unclear about a change, take a moment to explain the why behind it, not just the what. People adapt far better to decisions they understand, and you can extend that understanding past where the original explanation reached.	1	614
615	Turn the latest update into one adjusted priority, Updates only keep you aligned if you let them change something. After the next significant update, do not just absorb it. Ask one question: given this, what on my list should move up, down, or off? An update that reorders your actual priorities is doing its job. One that you simply read and nod at has changed nothing.	1	615
616	Ask the context question instead of waiting for it, When decisions arrive late and thin, the missing context rarely comes to you on its own. Go and get it. When a change lands without enough background, ask one direct question of whoever announced it: what is driving this, and what does it mean for my work? A short question gets you the context the announcement left out, and it gets it now rather than never.	1	616
617	Replace the guess with one direct question, Guessing at the reason behind a change wastes energy and usually guesses wrong. Stop guessing and ask. Pick the change that is bothering you most and put one plain question to the person who made it or announced it: what is the thinking behind this? Most people will explain if asked directly. The guessing continues mainly because nobody asks the question out loud.	1	617
618	Confirm your understanding before you build on it, Rework happens when you act on an unclear decision and find out later you read it wrong. Catch that before it costs you. Before you start the work, write your understanding of the change in two lines and send it to the relevant person: this is what I am taking from it, correct? Thirty seconds of confirmation is far cheaper than a day of redone work.	1	618
619	Write down the new knowledge so it does not fade, Knowledge picked up through work is the easiest kind to lose, because it never felt like studying. Pin it down. Take the most useful thing you have learned recently and write it as one or two plain lines somewhere you will find it again. The note is what separates something you genuinely know from something you happened to encounter and will half-remember in a month.	1	619
620	Use the next practice chance on your weakest part, Chances to practice are most valuable when you point them at what you are not yet good at, not what is already comfortable. Next time you get one, deliberately use it on the part you find hardest. Practising the easy bits feels productive and changes little. Practising the weak bit is mildly uncomfortable and is where the actual skill is built.	1	620
621	Tell your manager which learning is paying off, Learning that feels relevant to your role is more fragile than it looks, because what you get offered is set by other people's guesses. Protect the fit. Tell your manager which recent learning has actually been useful and why. Naming what is landing makes it far more likely the next opportunity is pointed the same way rather than drifting off target.	1	621
622	Make space by learning inside a current task, When there is no obvious space to learn something new, stop looking for a separate space and fold it into the work. Pick one current task and do a part of it in a way that requires learning something: a new method, a tool feature, a technique you have read about but never used. The space does not have to be found. It can be made inside what you already do.	1	622
623	Change one thing about a repetitive task this week, Repetitive work stops offering development when you run it the same way every time, but the repetition is not the only fixed thing. Pick one repetitive task and change a single element: do it faster, do it cleaner, automate a step, improve the output. The development moment is in the change you introduce, not in waiting for the task itself to become different.	1	623
624	Protect twenty minutes before the demands start, Learning gets squeezed out because it has no deadline and everything else does, so it always loses the moment the day starts. Move it before the day starts. Block twenty minutes early, before the demands arrive, for one specific piece of learning. Time taken at the front of the day is yours. Time you hope to find later has already been promised to something louder.	1	624
625	Tell your manager which work uses you best, Being trusted with work that uses your strengths is valuable and not guaranteed to continue, because assignments are made on rough guesses. Protect the fit. Tell your manager plainly which work plays to your strengths and why it goes well. Naming it makes the next assignment more likely to land in the same place rather than drifting somewhere that wastes what you do best.	1	625
626	Take one idea from thought to a small test, Having space to explore your ideas is only worth something if ideas actually move through it. Pick your most promising current idea and take it one concrete step: a small test, a quick prototype, a conversation with someone who could back it. An idea that gets tested becomes real or gets ruled out cleanly. An idea that only gets explored in your head stays a maybe.	1	626
627	Pick one growth option and take a first step, Sensing there are options to grow is good, and options only matter once you move on one. Choose the option that fits you best and take a single concrete step this week toward it: register interest, build a relevant skill, talk to someone who has done it. Options you act on open up. Options you only keep in mind tend to quietly stay options.	1	627
628	Ask for one task that uses your strongest skill, Strengths feel underused when the people assigning work do not have a clear picture of what you are best at. Give them one. Name your strongest skill to your manager and ask directly for one piece of work that would use it. A specific request tied to a specific strength is easy to act on. Hoping the right work finds you usually means it does not.	1	628
629	Bring the idea with a small first step attached, Ideas lose traction when they arrive as concepts that someone else has to figure out how to start. Remove that barrier. Next time you propose one, bring a small, concrete first step already worked out: here is the idea, and here is the low-cost thing we could do this week to test it. An idea with an easy first move is far more likely to actually begin.	1	629
630	Ask one person what the next step really takes, Opportunities to progress often feel limited because the path is unmapped, not because it is closed. Map one step of it. Find someone who has moved up from where you are and ask plainly what the step actually required: which skills, what experience, how they made the case. A path with a visible first move is something you can act on. An unmapped one only feels like a wall.	1	630
631	Be the quick help for someone else this week, Being able to ask for help and get it fast depends on a culture where people respond, and that culture is built by individual responses. Be one of them. This week, when a colleague needs something, make yours the quick reply, the fast hand. The responsiveness you rely on exists because people give it. Keeping it strong means giving it as readily as you receive it.	1	631
632	Check in on one colleague before they ask, People checking in and offering support before being asked is a rare, valuable habit, and you can add to it. Pick one colleague who seems stretched or quiet and check in genuinely: how are you doing, is there anything I can take. The support culture you benefit from is made of exactly these unprompted moments. Supplying one keeps it alive.	1	632
633	Tell your team the backing actually mattered, Feeling backed by your team when things are hard is worth naming out loud, because support that goes unacknowledged slowly fades. After a hard stretch where the team had your back, tell them specifically: this is what your support let me do. Naming it makes the team's instinct to back each other stronger, and makes them more likely to do it again next time.	1	633
634	Make the first ask small and specific, Asking for help feels hard when it sounds like admitting you cannot cope. Make the first one small enough that it does not. Pick something minor and specific, a quick question, a second opinion, and ask for just that. A small, defined ask is easy for you to make and easy for someone to answer. A few of those make the larger asks feel normal.	1	634
635	Line up one reliable person before you need them, Inconsistent support is hardest in the moment you urgently need it. Sort it out in advance. Identify one person who is genuinely reliable for the kind of help you tend to need, and build a bit of relationship with them before a setback hits. One dependable contact, arranged ahead of time, beats scrambling for whoever is available when something has already gone wrong.	1	635
636	Ask one person into the problem with you, Being left to manage challenges alone is partly circumstance and partly that nobody knows you are carrying them. Let one person in. Pick a colleague or manager and share one challenge you are handling solo, with a specific ask: a perspective, a hand, a check on your thinking. People rarely volunteer for a problem they cannot see. Naming it is what makes help possible.	1	636
637	Notice the prioritising habit that keeps it manageable, A workload that feels manageable is usually the result of a habit working well: a weekly plan, a clear ranking, a regular tidy of the list. Notice which habit is doing it for you and keep it deliberate. Workloads tip over quietly when the habit slips. Knowing exactly what is keeping yours steady means you can protect it when a busy week tries to crowd it out.	1	637
638	Protect whatever is giving you the focus time, Focus time that lets you make meaningful progress is the first thing a busy period erodes. Notice what is creating it: a blocked morning, a habit of closing channels, a meeting you keep clear. Then defend that specific condition deliberately next week, before the calendar fills. Focus time you actively protect survives. Focus time you assume will be there gets taken.	1	638
639	Use the calm to get ahead on something important, Being able to plan without constant disruption is a good window, and good windows are best spent getting ahead, not just keeping pace. Use the current calm to move one important but non-urgent thing forward. Disruption always returns. Work you pulled forward during a settled stretch is the buffer that keeps the next busy period from knocking you off course.	1	639
640	Get one priority confirmed as the genuine top, Competing priorities make focus impossible because you are quietly choosing between them all day, under pressure, alone. Take the choice out of your head. List them, take the list to whoever sets the work, and ask one question: if only one moves this week, which is it? A single confirmed top priority does not clear the list, but it tells you where to actually put your focus.	1	640
641	Show the full load before you take on more, A heavy, unpredictable workload stays that way because each new task is added on its own, with nobody seeing the whole. Make the whole visible. Next time you are asked to take something on, do not just say yes. Show what is already on your plate and ask what should move to make room. The trade-off was always there. You are making it a decision someone has to see.	1	641
642	Protect one block a day from interruption, Interruptions reduce productivity more than their length suggests, because each one costs the time to refocus afterward. Carve out one defended block. Pick one period each day, mark it as unavailable, close the channels, and use it for work that needs real concentration. You will not stop every interruption, but one protected block daily is where the meaningful work actually gets done.	1	642
649	Keep the habit that makes conversations two-way, Conversations that feel clear and genuinely two-way are produced by specific habits: asking before answering, checking understanding, leaving room for the other side. Notice which of those you do and keep doing it deliberately. Good conversations are not luck, and the ones around you stay good partly because of your part in them. Name your contribution so you can protect it.	1	649
650	Give one person the listening you value, Being genuinely listened to is rare enough that you notice it. You know how it feels, so pass it on. In your next conversation, give one person your full attention: no interrupting, no planning your reply while they talk, a clear acknowledgement of their point before you add yours. The experience of being understood spreads when people copy it, and you can start the copy.	1	650
651	Turn good communication into one acted-on thing, Communication that keeps you connected and aligned is doing its job when it changes what you do, not just what you know. After the next useful exchange, name one concrete thing you will do differently because of it. Communication that produces an action keeps you genuinely aligned. Communication you simply receive and file leaves you informed but not actually moved.	1	651
652	Reply with your read and ask them to confirm, When a message is unclear, the open question can you clarify often just returns more vagueness. State your best understanding instead: I am reading this as X, due Friday, is that right? Confirming a specific interpretation is faster for the other person than re-explaining from scratch, and you walk away with a precise answer and a written record of it.	1	652
653	Lead with your point and pick the right moment, Not feeling heard is often about timing and framing, both of which you control. Next time, open with your point in one clear sentence rather than building toward it, and choose a moment when the person can actually attend, not the end of a packed meeting. The same perspective, stated sharply at a moment someone can receive it, gets heard very differently.	1	653
654	Close conversations by confirming what was agreed, Most avoidable friction comes from people leaving a conversation with different versions of what was decided. Catch it at the exit. End your next few important conversations by briefly stating what you both agreed and what happens next, and checking it matches. Ten seconds of confirmation prevents the misalignment that turns into rework, blame, and friction a week later.	1	654
655	Write down the new knowledge so it does not fade, Knowledge picked up through work is the easiest kind to lose, because it never felt like studying. Pin it down. Take the most useful thing you have learned recently and write it as one or two plain lines somewhere you will find it again. The note is what separates something you genuinely know from something you happened to encounter and will half-remember in a month.	1	655
656	Use the next practice chance on your weakest part, Chances to practice are most valuable when you point them at what you are not yet good at, not what is already comfortable. Next time you get one, deliberately use it on the part you find hardest. Practising the easy bits feels productive and changes little. Practising the weak bit is mildly uncomfortable and is where the actual skill is built.	1	656
657	Tell your manager which learning is paying off, Learning that feels relevant to your role is more fragile than it looks, because what you get offered is set by other people's guesses. Protect the fit. Tell your manager which recent learning has actually been useful and why. Naming what is landing makes it far more likely the next opportunity is pointed the same way rather than drifting off target.	1	657
658	Make space by learning inside a current task, When there is no obvious space to learn something new, stop looking for a separate space and fold it into the work. Pick one current task and do a part of it in a way that requires learning something: a new method, a tool feature, a technique you have read about but never used. The space does not have to be found. It can be made inside what you already do.	1	658
659	Change one thing about a repetitive task this week, Repetitive work stops offering development when you run it the same way every time, but the repetition is not the only fixed thing. Pick one repetitive task and change a single element: do it faster, do it cleaner, automate a step, improve the output. The development moment is in the change you introduce, not in waiting for the task itself to become different.	1	659
660	Protect twenty minutes before the demands start, Learning gets squeezed out because it has no deadline and everything else does, so it always loses the moment the day starts. Move it before the day starts. Block twenty minutes early, before the demands arrive, for one specific piece of learning. Time taken at the front of the day is yours. Time you hope to find later has already been promised to something louder.	1	660
661	Write down the difference so it survives a hard week, Seeing that your work makes a difference is motivating now, and that feeling does not survive a stressful week on its own. Give it something solid. Write one specific example of the difference your work made, who it helped and how. On the days the meaning feels thin, a concrete record carries more weight than trying to remember why it mattered.	1	661
662	Tell one colleague the value you see in their work, Feeling that your contributions are valued is worth passing on, because plenty of people doing purposeful work cannot see it themselves. Pick one colleague and tell them specifically how their work matters: the difference it makes, who it helps. You know how much that recognition is worth because you are feeling it. Giving it costs a sentence and lands harder than you expect.	1	662
663	Name what makes the work feel worthwhile, Motivation that comes from worthwhile work is strong while it lasts, and it lasts longer if you understand its source. Name precisely what makes this work feel worth doing: the people it serves, the problem it solves, the craft of it. Knowing the source means that when motivation dips, you can deliberately reconnect with it rather than waiting for it to return on its own.	1	663
664	Trace one task to the person it affects, Impact is hard to see when your work hands off before you can watch what it does. Trace one piece yourself. Pick a recent task and follow the thread: who received it, what they did with it, what changed because of it. You may need to ask one person downstream. The impact usually exists. It is just past the point where your own view normally stops.	1	664
665	Ask how your work connects to a goal you care about, Work feels disconnected from meaningful outcomes when nobody has drawn the line between your task and the larger goal. Ask for the line. Pick someone who sees the bigger picture and ask directly how what you do feeds into something that matters. The connection often exists and was simply never explained. Once you can see it, the same work stops feeling disconnected.	1	665
666	Rebuild purpose from the part of the work you control, When lost purpose has drained your motivation, waiting for it to return rarely works. Rebuild it from something smaller and closer. Pick one part of your work you genuinely control and find the meaning in it: the quality you can put in, the colleague you can help, the problem you solve well. Purpose reassembles from the pieces you can act on, not the picture you cannot.	1	666
667	Make space for someone who is not at ease yet, Feeling accepted and able to be yourself is something not everyone around you has, especially people who are newer or different from the group. Use your ease to widen the room. Make one deliberate move this week to help someone less settled feel they belong: include them, ask their view, make a connection. Acceptance grows when the people who have it extend it.	1	667
668	Show empathy first when a colleague is struggling, Empathy when challenges arise is built from individual responses, not policy, and you are one of the responders. Be deliberate about it. Next time a colleague is visibly having a hard time, lead with the person before the task: a genuine question, a real offer of help. Each time someone responds that way, it becomes a little more the normal here.	1	668
669	Use the trust on something genuinely difficult, Trust to share difficult things openly is only worth having if you use it for the genuinely difficult, not just the mildly awkward. This week, if there is something real you have been holding back, share it with the right person. Trust that only ever carries easy things slowly fades. Used on something that matters, it gets stronger and proves itself real.	1	669
670	Lower the guard once with one trusted person, Being guarded keeps you safe and also keeps you slightly apart, because people connect with what you let them see. You do not have to drop the guard everywhere. Pick one person who has earned some trust and share one slightly more honest thing than usual. Connection deepens through small, chosen disclosures. A guard that never lowers guarantees the distance stays.	1	670
671	Tell one person plainly what you need from them, When empathy and understanding feel absent, people are often not unkind, just unaware, because they cannot see what you have not shown them. Give one person something to respond to. Tell a colleague you trust, plainly, what is hard right now and what would help. Understanding usually follows knowing. People cannot meet a difficulty you have kept invisible.	1	671
672	Rebuild engagement from one safe interaction, When low acceptance has worn down your confidence, withdrawing further feels safer and quietly makes it worse. Rebuild in small steps. Pick one low-risk interaction this week, a conversation with someone safe, a small contribution in a friendly setting, and engage just there. Confidence comes back through repeated small, positive contact, not through waiting until you feel ready.	1	672
673	Notice what gives you clarity and keep asking for it, Having the clarity and support to perform well is not the default, so it is worth knowing what produces it. Notice the specifics: the brief that was clear, the question that was answered fast, the access you were given. Then keep asking for those same things by name on the next piece of work. Clarity you can describe is clarity you can request again.	1	673
674	Flag the next dependency before it becomes a blocker, Dependencies get handled efficiently partly because someone raises them early. Keep being that someone. Look ahead on your current work and name the next thing you will need from someone else, then flag it now rather than when you reach it. A dependency raised early is a scheduling note. The same dependency raised late is a blocker. The difference is who looks ahead.	1	674
675	Protect the conditions that keep the path clear, Steady progress without major blockers usually has causes worth protecting: good handoffs, early planning, the right access in place. Notice which conditions are keeping your path clear and treat them as things to maintain, not luck. A clear run tends to end when one of those quietly slips. Knowing what they are means you can catch the slip before it becomes a blockage.	1	675
676	Name the recurring blocker and tackle its cause, Blockers that keep recurring are usually one or two specific things, not bad luck. Find the pattern. For a week, note what actually stops you each time. One cause will likely repeat. Then tackle that single root: the approval that is always slow, the information always missing. Fixing one recurring blocker at its source clears more time than firefighting each instance as it lands.	1	676
677	Make one specific list of exactly what you need, Missing information, access, or support stays missing when the need is vague. Make it precise. Write one short, specific list: exactly what you need, from whom, by when, and why it is blocking you. Then send it to the people who can act. A clear, itemised request is hard to ignore and easy to fulfil. A general I do not have what I need is neither.	1	677
678	Look for the constraint that effort cannot fix, When strong effort still does not produce delivery, the problem is usually not effort, and adding more will not help. Look for the constraint. Step back and find the one thing genuinely in the way: a missing decision, a broken process, an unrealistic scope. Name it. A constraint you have identified can be raised and addressed. Working harder against it just costs more for the same result.	1	678
679	Sharpen the question your reflection asks, A reflection habit you already keep is worth improving rather than just continuing. Most reflection asks how did it go, which is too broad to produce much. Swap it for something sharper: what did I do that worked and should repeat, and what cost me more than it should have. A better question is what turns a steady habit into one that actually changes how you work.	1	679
680	Turn the next reflection into one concrete change, Reflection that helps you reset and prioritise is doing real work, and it does the most when it ends in a change. After your next reflection, do not just feel clearer. Name one specific thing you will do differently because of it, and start it. Reflection that produces a change compounds. Reflection that only produces a calmer mind resets you to the same place.	1	680
681	Write the direction down so it survives the week, Taking stock gives you clarity, and clarity is the first thing a busy week erodes. Make it durable. When a moment of reflection gives you a clear direction, write it in one or two plain lines and keep it where you will see it. The note holds the direction steady when the daily noise would otherwise scatter it within a few days.	1	681
682	Shrink reflection to one question at day's end, Reflection loses to a busy week because it sounds like a session you have to find time for. Shrink it until it fits the time you already have. Before you close your laptop, answer one question: what is the most useful thing I learned today? Sixty seconds, no notebook. Done daily, that is more genuine reflection than most people manage in a quiet month.	1	682
683	Attach reflection to something already in your day, Waiting for reflection to be built into how the team works means waiting indefinitely. You do not need the routine to change, you need to attach to it. Pick something that already happens every day, the commute, the first coffee, the end of a call, and make that your reflection cue: one question, same moment, every day. The trigger already exists. Give it a second job.	1	683
684	Run a two-minute review at the end of one task, Missed learning is the quiet cost of finishing a task and moving straight to the next. Insert a small pause. At the end of your next significant task, before moving on, spend two minutes on two questions: what worked here, and what would I do differently. That short review is where the learning gets captured. Without it, the experience happens and teaches you nothing.	1	684
685	Keep the habit that makes conversations two-way, Conversations that feel clear and genuinely two-way are produced by specific habits: asking before answering, checking understanding, leaving room for the other side. Notice which of those you do and keep doing it deliberately. Good conversations are not luck, and the ones around you stay good partly because of your part in them. Name your contribution so you can protect it.	1	685
686	Give one person the listening you value, Being genuinely listened to is rare enough that you notice it. You know how it feels, so pass it on. In your next conversation, give one person your full attention: no interrupting, no planning your reply while they talk, a clear acknowledgement of their point before you add yours. The experience of being understood spreads when people copy it, and you can start the copy.	1	686
687	Turn good communication into one acted-on thing, Communication that keeps you connected and aligned is doing its job when it changes what you do, not just what you know. After the next useful exchange, name one concrete thing you will do differently because of it. Communication that produces an action keeps you genuinely aligned. Communication you simply receive and file leaves you informed but not actually moved.	1	687
688	Reply with your read and ask them to confirm, When a message is unclear, the open question can you clarify often just returns more vagueness. State your best understanding instead: I am reading this as X, due Friday, is that right? Confirming a specific interpretation is faster for the other person than re-explaining from scratch, and you walk away with a precise answer and a written record of it.	1	688
689	Lead with your point and pick the right moment, Not feeling heard is often about timing and framing, both of which you control. Next time, open with your point in one clear sentence rather than building toward it, and choose a moment when the person can actually attend, not the end of a packed meeting. The same perspective, stated sharply at a moment someone can receive it, gets heard very differently.	1	689
690	Close conversations by confirming what was agreed, Most avoidable friction comes from people leaving a conversation with different versions of what was decided. Catch it at the exit. End your next few important conversations by briefly stating what you both agreed and what happens next, and checking it matches. Ten seconds of confirmation prevents the misalignment that turns into rework, blame, and friction a week later.	1	690
691	Turn the encouragement into one booked commitment, Being encouraged to develop is permission, and permission only matters once it becomes a commitment. Convert it. Pick one specific thing to improve and book real time for it this week, treated like any other commitment. Encouragement that turns into a scheduled action produces progress. Encouragement that stays a good feeling leaves your skills exactly where they are.	1	691
692	Use one resource on your weakest area rather than your strongest, Resources and coaching help most when you point them at what you are not yet good at. It is tempting to use them on what already interests you. Resist that. Pick the area you are weakest in and aim one resource or one coaching conversation squarely at it. Development happens fastest where the gap is largest, which is rarely the comfortable place.	1	692
693	Record the progress so it keeps motivating you, Seeing progress in your capability is motivating, and the proof of it fades because each new skill quickly becomes the normal you forget you lacked. Keep a record. Every month or so, note one thing you can now do that you could not before. That short list is concrete evidence of progress, and it carries you through the stretches where growth feels invisible.	1	693
694	Drive your own development with one small weekly step, When development is not prioritised around you, waiting for that to change means standing still. Drive it yourself. Pick one skill and take one small step toward it every week: a task that uses it, twenty minutes of practice, a person to learn from. Development does not actually require a programme. It requires one consistent step, and that step is entirely yours to take.	1	694
695	Find your own resource for the skill you actually want, When the resources on offer are hard to reach or off-target, stop letting the available menu define your learning. Start from the skill. Name exactly what you want to get better at, then go find your own way to learn it: a specific person, an article, a small project that demands it. The relevant resource usually exists. It is just not on the list you were handed.	1	695
696	Set one concrete growth target with a deadline, Growth feels too slow when it is vague, because vague progress is impossible to measure or push. Make it concrete. Set one specific, modest target with a date: by the end of next month I will be able to do X. A defined target gives you something to aim at and a way to know you are moving. Pace improves once growth stops being a feeling and becomes a goal.	1	696
697	Pick one progression option and start preparing, Seeing clear options for progression is only useful if you move toward one. Choose the option that fits you best and start preparing now: the skill it needs, the experience it expects, the people who should know you are interested. A path you are actively preparing for tends to open. One you are only keeping in view tends to stay a view.	1	697
698	Use the encouragement to have one real conversation, Being encouraged to explore pathways is permission, and permission only matters when used. Have one real conversation this month: ask someone in a role you might want how they got there and what it takes. Encouragement that turns into a concrete conversation moves you forward. Encouragement that stays a good feeling leaves you exactly where you are.	1	698
699	Name the role that fits and ask for the support, Support to move toward roles that fit your strengths works best when you can tell people exactly what you are aiming at. Get specific. Name the kind of role that fits you, then ask for one concrete piece of support toward it: an introduction, a stretch task, a development conversation. Specific support is easy to give. A general wish to progress gives people nothing to help with.	1	699
700	Ask someone what the next step really requires, Progression feels out of reach largely because the path is unmapped, and you cannot move toward something you cannot see. Map one step of it. Find someone a level ahead and ask plainly what their step up actually required: which skills, what experience, how long. A path you can see has a first move on it. An unclear one only has a vague distance.	1	700
701	Find one person outside the chain to ask, If your manager is not supporting you to explore new paths, they do not have to be your only route. Look wider. Find one person elsewhere in the organisation doing something that interests you and ask for fifteen minutes to hear about it. Exploration does not require permission from above. It requires one willing person to talk to, and there is usually one.	1	701
702	Spend thirty minutes finding what actually exists, Being unsure what opportunities exist usually means you have not yet gone looking, not that there are none. Go looking. Spend thirty minutes this week finding out: check internal listings, ask two well-connected colleagues, look at where people in roles like yours have moved. Thirty minutes of deliberate searching replaces a vague uncertainty with an actual list of options.	1	702
709	Notice what gives you clarity and keep asking for it, Having the clarity and support to perform well is not the default, so it is worth knowing what produces it. Notice the specifics: the brief that was clear, the question that was answered fast, the access you were given. Then keep asking for those same things by name on the next piece of work. Clarity you can describe is clarity you can request again.	1	709
710	Flag the next dependency before it becomes a blocker, Dependencies get handled efficiently partly because someone raises them early. Keep being that someone. Look ahead on your current work and name the next thing you will need from someone else, then flag it now rather than when you reach it. A dependency raised early is a scheduling note. The same dependency raised late is a blocker. The difference is who looks ahead.	1	710
711	Protect the conditions that keep the path clear, Steady progress without major blockers usually has causes worth protecting: good handoffs, early planning, the right access in place. Notice which conditions are keeping your path clear and treat them as things to maintain, not luck. A clear run tends to end when one of those quietly slips. Knowing what they are means you can catch the slip before it becomes a blockage.	1	711
712	Name the recurring blocker and tackle its cause, Blockers that keep recurring are usually one or two specific things, not bad luck. Find the pattern. For a week, note what actually stops you each time. One cause will likely repeat. Then tackle that single root: the approval that is always slow, the information always missing. Fixing one recurring blocker at its source clears more time than firefighting each instance as it lands.	1	712
713	Make one specific list of exactly what you need, Missing information, access, or support stays missing when the need is vague. Make it precise. Write one short, specific list: exactly what you need, from whom, by when, and why it is blocking you. Then send it to the people who can act. A clear, itemised request is hard to ignore and easy to fulfil. A general I do not have what I need is neither.	1	713
714	Look for the constraint that effort cannot fix, When strong effort still does not produce delivery, the problem is usually not effort, and adding more will not help. Look for the constraint. Step back and find the one thing genuinely in the way: a missing decision, a broken process, an unrealistic scope. Name it. A constraint you have identified can be raised and addressed. Working harder against it just costs more for the same result.	1	714
715	Sharpen the question your reflection asks, A reflection habit you already keep is worth improving rather than just continuing. Most reflection asks how did it go, which is too broad to produce much. Swap it for something sharper: what did I do that worked and should repeat, and what cost me more than it should have. A better question is what turns a steady habit into one that actually changes how you work.	1	715
716	Turn the next reflection into one concrete change, Reflection that helps you reset and prioritise is doing real work, and it does the most when it ends in a change. After your next reflection, do not just feel clearer. Name one specific thing you will do differently because of it, and start it. Reflection that produces a change compounds. Reflection that only produces a calmer mind resets you to the same place.	1	716
717	Write the direction down so it survives the week, Taking stock gives you clarity, and clarity is the first thing a busy week erodes. Make it durable. When a moment of reflection gives you a clear direction, write it in one or two plain lines and keep it where you will see it. The note holds the direction steady when the daily noise would otherwise scatter it within a few days.	1	717
718	Shrink reflection to one question at day's end, Reflection loses to a busy week because it sounds like a session you have to find time for. Shrink it until it fits the time you already have. Before you close your laptop, answer one question: what is the most useful thing I learned today? Sixty seconds, no notebook. Done daily, that is more genuine reflection than most people manage in a quiet month.	1	718
719	Attach reflection to something already in your day, Waiting for reflection to be built into how the team works means waiting indefinitely. You do not need the routine to change, you need to attach to it. Pick something that already happens every day, the commute, the first coffee, the end of a call, and make that your reflection cue: one question, same moment, every day. The trigger already exists. Give it a second job.	1	719
720	Run a two-minute review at the end of one task, Missed learning is the quiet cost of finishing a task and moving straight to the next. Insert a small pause. At the end of your next significant task, before moving on, spend two minutes on two questions: what worked here, and what would I do differently. That short review is where the learning gets captured. Without it, the experience happens and teaches you nothing.	1	720
862	Mark a win privately and invite one colleague in. When your team never pauses to celebrate the celebration still has to happen just not at team level. Mark your wins for yourself end of week end of month end of quarter. Then occasionally invite one trusted colleague in: I am marking that we got X done want to grab a coffee? Two-person celebrations seed team ones and meanwhile you keep your own sense of momentum intact.	1	1149
863	Send a Friday note that includes one win. Managers who do not acknowledge successes often miss them because they were never told. Tell them briefly. Send a Friday note that includes one thing that went well that week no fanfare: this came together. I am pleased with how X worked. The note creates the moment of recognition that would not have happened spontaneously and over time it trains them to start noticing without being prompted.	1	1150
864	Insert your own pause when a piece of work lands. Moving straight to the next thing is the default and you can insert your own pause even when the team does not. When a piece of work lands take ten minutes before starting the next: write what worked what you learned what you would do again. The pause does not need anyone else. It is the difference between work that accumulates as a list of done items and work that adds up to progress.	1	1151
871	Browse the catalogue quarterly even when content. An easy-to-browse learning catalogue only helps if you visit it and most people only look when they have a specific need. Build a quarterly browse into your routine: twenty minutes no agenda. You will spot things you would not have known to search for and keep your sense of what is available current. The catalogue is the resource. The habit of looking is what makes it work for you.	1	1161
872	Share what you are curious about so they can point better. A manager who points you to learning options gives better pointers when they know what you are curious about. Share that regularly. Mention one area you would like to learn more about at your next 1:1 even informally. The information lets your manager surface options you would not have found and turns occasional pointing into pointing that consistently matches what you actually want to know.	1	1162
873	Share back one resource that helped you. When colleagues share useful learning resources the supply holds because people reciprocate. Be a reciprocator. The next time you read or watch something genuinely useful for the work you all do send it to one or two people who would benefit with one line about why. Peer learning networks work because individuals keep posting. They die quietly when only a few people do.	1	1163
874	Maintain your own personal learning directory. When there is no clear place to find learning build your own. Keep a personal directory: the books articles courses and people that have actually taught you something useful. Add to it as you find more. Your own curated list is more valuable than any company catalogue because it is filtered by what worked for you and it is fully yours regardless of what your employer does or does not provide.	1	1165
875	Ask peers in your role who they learned from. When your manager never signposts learning peers in your role usually have a list. Ask three of them separately one question: what is the single most useful thing you have learned from in this role? You will get three different answers and one or two will be exactly what you needed. The shortcut to good learning is usually a peer who has already done the search you have not.	1	1166
876	Follow one expert deeply instead of hunting widely. When learning is hard to find hunting widely costs more time than it produces value. Switch to depth. Pick one expert in the area you care about find what they have written watched taught or said and work through it in order. One person followed deeply gives you more than ten sampled lightly and the depth keeps building on itself in a way that scattered hunting does not.	1	1167
878	Take the push and tell your manager what came of it. A manager who pushes you to try new things keeps pushing when they see the results. Show them. After the push tell them what came of it: I tried what you suggested here is what happened here is what I am taking from it. The reporting is what keeps the push coming. Without it pushes that landed look the same as pushes that did not and the practice fades.	1	1170
879	Volunteer for one unfamiliar thing each quarter. Volunteering for something unfamiliar is a muscle that needs regular use and it weakens fast when you let comfort dictate which work you take on. Set a target: one genuinely unfamiliar thing per quarter. It does not have to be huge. It has to be outside what you would normally pick. The target is what keeps you stretching deliberately rather than only when something happens to push you.	1	1171
880	Name what would stretch you and ask for it. Stretch work usually has to be claimed rather than waited for because the people allocating work do not always know what would stretch you. Help them out. Identify one specific thing that would push you beyond your current level a project a presentation a problem to solve and ask your manager directly for it. A named specific ask is easy to act on. Waiting for the right stretch to appear rarely produces one.	1	1173
958	Find one person you can be honest with first. Inside or outside the team works equally well. Safety to share isn't binary across a whole company it lives in specific relationships. Identify one trusted colleague mentor or peer in another team and share what you're sitting with. A single honest channel beats none.	1	1277
881	Volunteer for stretch outside your manager's allocation. If your manager keeps you in safe familiar tasks you can still find stretch outside their allocation. Volunteer for a cross-functional project a working group a piece of work in another team that is short-handed. Your manager is not the only source of work in the org and stretch claimed from elsewhere builds you in ways your own manager will eventually have to recognise.	1	1174
882	Create your own stretch with an unowned problem. When no chances to stretch yourself appear the route is to create one. Look around for a problem nobody owns even a small one: a process that needs improving a piece of analysis nobody has done a tool that needs setting up. Pick it up. Self-created stretch counts as much as offered stretch and you get to do it without waiting for the org to notice that you were ready.	1	1175
889	Deliver consistently to keep the trust intact. Being trusted to decide how you do your work rests on a track record and the trust shrinks the first time the delivery slips. The best way to keep the autonomy is to make the outcomes so reliable that no one has any cause to look at how you work. Output is what bought you the freedom. Output is what keeps it. A few missed promises is what makes managers start asking about process.	1	1185
890	Make outcomes very visible so process does not get checked. A manager who focuses on outcomes rather than micromanaging gives you what most people would want and the way to keep it is to make outcomes very visible. Update them clearly and regularly on what is shipping. When outcomes are obvious your manager has no reason to ask about process. When outcomes go quiet even an outcome-focused manager will start looking at how you work to figure out what is happening.	1	1186
891	Bring real discipline to the freedom. Being able to organise your own time and approach is freedom that only works if you bring real discipline to it. Without internal structure the freedom turns into drift. Build your own routines: when you plan when you focus when you review. Self-managed time demands more discipline than externally managed time rather than less. The discipline is what keeps the freedom from quietly costing you.	1	1187
892	Over-communicate proactively to reduce check-ins. Micromanagement usually intensifies when the manager feels they have low visibility and decreases when they have high visibility. Pre-empt them. Send a short proactive update twice a week before they ask: here is what I am working on here is what I am hitting next. The update gives them what the micromanagement was trying to extract and most managers reduce check-ins when they can already see what is happening.	1	1189
893	Bundle small decisions into one weekly check. Managers who check every small decision do it partly because the decisions come one at a time. Bundle them. Hold the small decisions for a single weekly check-in: here are five things I plan to do this way unless you flag anything. Most managers will skim and approve and over time the bundling teaches them that you handle the small stuff sensibly and they stop wanting to see every one.	1	1190
894	Find the latitude that already exists inside the rules. Rigid rules usually have more latitude inside them than the surface suggests and people work around the surface rather than the actual constraint. Ask. Find the person who owns the rule and ask what it is trying to prevent. The answer almost always reveals room you did not know was there. Operating cleverly within the rule gets you the freedom you wanted without changing the rule itself.	1	1191
901	Ask one follow-up when leaders explain reasoning. When leaders explain the reasoning behind decisions the explanation only lands if people engage with it rather than nodding through it. Be one of the engagers. Ask one follow-up question that pushes deeper: what would have changed the decision the other way? Genuine engagement encourages leaders to keep explaining and gets you a richer understanding than the public explanation alone.	1	1201
902	Bring specific decisions to your manager for the why. A manager who helps you understand the why does it more easily when you bring specific decisions to them rather than asking in the abstract. Before a 1:1 identify one decision that affects your work that you do not fully understand the reasoning for and ask. Specific questions get specific answers and over time you build a sense of how decisions are being made rather than just what they are.	1	1202
903	Share your own reasoning back when it is relevant. Openly shared thinking behind decisions works as a two-way exchange. Add to it. When you have a view on something that affects your work share your reasoning openly too: here is how I am thinking about this here is what I am missing. The culture of shared thinking holds when individuals at every level contribute their reasoning rather than just receive what others share.	1	1203
904	Ask one direct question for the missing explanation. Decisions that arrive with no explanation often have one available and nobody has asked. Ask calmly and once. Help me understand the reasoning here. What changed? Most decisions have a why and most decision-makers will share it if asked directly not because they expect to be challenged but because the explanation was never explicitly requested. One direct question often unlocks what the announcement left out.	1	1205
905	Find the source of the decision yourself. If your manager cannot explain the reasoning behind a decision the reasoning still exists just not with them. Find the source. Identify who actually made the call and either ask your manager to find out or with their permission ask directly. Most decision-makers respond to a sincere polite request to understand the why. Sitting with no explanation when an explanation exists upstream is unnecessary.	1	1206
906	Build your own model of how decisions actually get made. When real reasons are kept behind closed doors build your own model of how decisions actually get made through observation. Notice patterns: who is in the room what seems to drive outcomes who tends to be right after the fact. Cross-check with one trusted senior person you can ask. A reliable working model is more useful than an official explanation and it makes the closed doors much less disorienting.	1	1207
913	Reconnect to the mission in moments of doubt. A mission that gives your work clear meaning is most useful in the harder moments when execution is grinding. Build a small habit. When you hit a stretch of work that feels meaningless take sixty seconds to remind yourself how it connects to the mission you signed up for. The connection is what carries you through the part where the work itself is not its own reward.	1	1217
914	Ask for the fit on the work that feels disconnected. A manager who shows you how your work fits the goal can do more of it on the pieces where the connection is least obvious to you. Ask. When a piece of work feels disconnected from the goal you understand raise it: I cannot see how this connects can you walk me through it? Sometimes there is a real connection you missed. Sometimes you have surfaced something worth changing.	1	1218
883	Use the connecting time for actual connection. Real time to connect with colleagues rather than just transact is the rare opportunity and the easy slip is filling it with work conversation anyway. Resist that. When you have unstructured time together ask something genuine: what are you working on that you are excited about what is going on outside work. The space was created for this. Letting it default back to status updates wastes the very thing that was scarce.	1	1177
884	Show up properly when the rituals come around. Team rituals only build bonding if people show up properly when they happen and the easy thing is to half-participate phone in hand mind on work. Make yours full. The next ritual whatever it is attend with attention. Rituals work because everyone present is actually present. They become hollow when most people are half-there and the bonding they were designed to build fades.	1	1178
885	Bring more than your work self to team time. A manager who fosters genuine team relationships does the work of creating the conditions and the relationships only form when you bring more than your work self. Share something real when team time comes around: an interest a setback a thing you are working on. Real relationships build on real disclosure. Surface participation in deep settings produces surface relationships no matter how good the manager's intention.	1	1179
886	Ask one non-task question per conversation. When you only ever interact about tasks the way to break the pattern is small and consistent. Ask one non-task question in each work conversation: how is your week going what are you up to this weekend are you reading anything good. It costs ten seconds and it gradually shifts the relationship from purely transactional to one with even a small amount of human texture underneath.	1	1181
887	Book one coffee with one colleague each week. When there is no time or space to connect you can create one micro-moment that does not need permission. Book a fifteen-minute coffee with one colleague each week. Just one. Over a year that is fifty different conversations you would not have had. The connections you would have if connection were properly resourced are reachable through small consistent moves just slower.	1	1182
888	Pick one distant colleague to know well. Setups that keep people distant make distance the default and closeness becomes something you have to choose. Choose one person. Identify a colleague in another office time zone or function and build a real working relationship with them anyway through regular video calls shared work and conversation that goes beyond the agenda. One person you know well across the distance is worth ten you barely see in your own office.	1	1183
895	Anchor each strength and gap to a real example. Taking regular stock of your strengths and gaps gets sharper when each one is anchored to a concrete example rather than held in the abstract. Next time you take stock for each strength write one situation where it showed up and for each gap one situation where it cost you. Specifics turn a vague self-image into something you can work with in reviews in 1:1s and in choosing what to develop next.	1	1193
896	Bring your own assessment to the review first. Open development reviews with your manager work best when you arrive with your own assessment ready rather than waiting to receive theirs. Before each one write your honest read on yourself: what you are getting right where you are weaker what you want to focus on. Arriving with your view first turns the review into a conversation and means your development is shaped by your reading of yourself rather than only theirs.	1	1194
897	Schedule the rerun of the framework annually. Using a tool or framework to assess yourself only stays useful if you rerun it periodically because the picture moves. Schedule the next pass: once a year is enough for most frameworks more often if your role is changing fast. The first run gives you a baseline. The second gives you a direction and the direction is what tells you whether the development you have done is showing up where it should.	1	1195
898	Do a thirty-minute self-audit this week. Self-assessment fits into much less time than it sounds. Block thirty minutes this week. List your three top strengths with one example each and your two biggest gaps with one example each. That is the audit. It is not perfect and it is much better than the no-data picture you have been working from and it gives you a starting point for the next conversation with your manager.	1	1197
899	Book the development review yourself. If you and your manager never review your development the absence usually persists by default rather than by choice. Book the meeting yourself. Send a calendar invite for thirty minutes with one subject line: development review. Bring a draft of your own assessment so the conversation has something to work with. Most managers will engage when the meeting is in the diary and the material is prepared.	1	1198
900	Ask three colleagues for one strength and one gap. Without a clear read on your strengths and gaps the fastest way to get one is to ask the people who see you work. Ask three colleagues separately two questions each: what is one thing I do well and what is one thing you would change. Three answers usually surface the same one or two themes and those are your read. Faster and more accurate than any framework you would work through alone.	1	1199
907	Capture this week's learning in one sentence. Learning new through work this week is the unusual case and it fades unless you capture it. Write one sentence: this week I learned X and here is when it will be useful. Sixty seconds no notebook needed. A captured sentence becomes a piece of knowledge you carry forward. An uncaptured one becomes a vague sense that the week went well with no specifics by Monday.	1	1209
908	Go one step past the surface exposure. Exposure to a new area or method is the start of learning rather than the end. Take it one step further. After the exposure spend twenty minutes reading or asking about it more deeply: what is the underlying principle what are the gotchas who knows it well. One step past the surface is what distinguishes I was exposed to it from I actually understand it and it costs almost nothing to do.	1	1210
909	Reciprocate by teaching them something you know. A colleague or mentor showing you something is a generous act and the way to keep that flow open is to be one too. Within the next month share something you know with them: a technique you use an article that changed your thinking a tool that saves time. Reciprocal teaching keeps the relationship two-way and useful rather than turning into one-sided receiving that fades.	1	1211
910	Do one familiar task differently this week. When the week's work is all familiar ground the variation has to come from inside the tasks themselves. Pick one and do it differently from how you usually do it: a different tool a different sequence a different person to involve. The work gets done either way. The difference is whether you produced one piece of learning along the way or none.	1	1213
911	Create twenty minutes of new exposure each week. Without exposure to anything new in your work the alternative is to create it. Twenty minutes a week of deliberate exposure outside your current ground: a podcast a paper a conversation with someone in another field. The exposure adds up over the year and it makes you the person on the team who brings ideas in from outside which is its own kind of advantage.	1	1214
912	Book one chat with someone outside your team. When there is no one to learn from in your immediate team the network expands as wide as you make it. Book one thirty-minute chat with someone outside your team whose work you respect in or out of the company. Most people will accept a sincere request to learn from them. The team you happen to sit in does not have to be the limit of who teaches you.	1	1215
919	Ask when you actually need to so the norm holds. A culture where asking for help is completely normal stays that way as long as people keep asking when they need to. Make sure you are one of them. Next time you genuinely need help ask even if you would normally try to push through alone. Norms hold because individuals supply the behaviour. A few people quietly defaulting to going it alone is enough to make others wonder if the norm is still real.	1	1225
920	Bring the ask earlier before it becomes a rescue. A manager who welcomes help requests makes it possible to bring them early before they become rescue missions. Use that. Next time you can see a problem coming ask while it is still small: I want to flag this early here is what I am thinking. Early asks are much more useful than late ones and a welcoming manager is wasted if you save your asks for emergencies.	1	1226
921	Be the team member others know they can ask. When your team responds well to help requests the responsiveness only holds if everyone supplies it. Make sure you do. When a colleague asks for help respond well: take it seriously give it real time follow up on whether the help landed. Reciprocal availability is what keeps the team responsive. One-way help-receiving from a team that helps slowly erodes the very thing you are benefiting from.	1	1227
922	Frame the ask as ownership of the outcome. Where asking for help is seen as weakness the way to ask without paying the cost is to frame the ask as ownership. Not I cannot do this but I am driving this and want to get it right can I run it past you? The framing makes you look like the person taking responsibility rather than the person struggling. Same conversation different signal to the person hearing it.	1	1229
923	Route help requests around your manager when needed. When your manager reacts poorly to help requests you can still get help. Just not from them. Build a list of three or four people who you can go to first: peers a senior individual an expert in another team. Most of what your manager would help with those people can help with too. You take what cannot be routed around to your manager only after you have tried the other paths.	1	1230
924	Build one reliable help-pair outside the team. An unresponsive team means broadcast asks go unanswered and the answer is to stop broadcasting. Build one reliable help-pair: a single colleague in your team or out who you have an explicit two-way arrangement with. You help each other quickly when asked. One responsive person covers most of what an unresponsive team will not and you control whether the pair exists unlike the team.	1	1231
931	Bring one well-framed recurring issue to your next 1:1. The space exists because you use it well so respect it by arriving with one clearly framed problem rather than a list. Naming the pattern in two sentences and the cost in one makes the conversation productive instead of venting.	1	1241
932	Match your manager's openness with a concrete proposal not just the problem. Openness to tackling persistent problems is rare. Don't waste it by handing over a complaint hand over the issue with a suggested first step. That moves the conversation from listening to deciding and earns more openness next time.	1	1242
933	Update the shared sticking-points list before your next 1:1 even briefly. Joint tracking only works if both of you keep it warm. Add one note about progress one about what's still blocked one open question. Five minutes of upkeep keeps the system alive and stops it becoming a graveyard list.	1	1243
934	Send the bigger issue in writing before your 1:1 lands. If the meeting won't hold it the meeting isn't the place. Write a short paragraph naming the recurring problem what you've tried and what you want from your manager. They can read it cold and arrive ready to actually discuss it.	1	1245
935	Frame the recurring problem with its cost. Brushing off becomes harder when the cost is named. Quantify what the issue is costing in time output or morale and put that in your next message. Brush-offs survive on vagueness a single concrete number forces a real response one way or the other.	1	1246
936	Start your own tiny follow-up log. Accountability flows in two directions. Each time a recurring issue is raised note the date and what was agreed. Bring it to the next conversation. You aren't checking up on your manager you're giving the system a memory it currently lacks.	1	1247
943	Choose your next skill before someone chooses one for you. The culture creates space for ambition not just compliance. Identify the capability that would genuinely move your work forward in the next quarter and start a small learning plan around it. Self-chosen skills tend to stick where assigned ones don't.	1	1257
944	Tell your manager what you're learning and what you've parked. Encouragement deserves a return signal. A short update on what you started what you finished what you abandoned and why keeps them invested. It also gives them new material to encourage you with rather than guessing what you'd find useful.	1	1258
945	Treat the learning budget as a tool with a deadline. Money or time unused tends to disappear. Plan how you'll spend this quarter's allocation in the next two weeks rather than the last two. Concrete plans get approved vague intentions get diary-bumped until the budget cycle closes around them.	1	1259
946	Pick a public project that requires the skill and learn forward. If the culture doesn't push you to grow pull yourself by signing up for something visible that you can't quite do yet. The commitment forces the learning and the visibility shifts how skill-building gets seen across the team.	1	1261
947	Document your own growth. You might be the only person tracking it but the record matters. Note what you've taken on what you've improved what you can now do that you couldn't six months ago. That log feeds your CV your case for a move and your sense of progress.	1	1262
948	Find free or low-cost learning that fits in fifteen minutes a day. Time and budget aren't the real blocker design is. A daily podcast on the commute or a chapter at lunch compounds faster than you expect. Don't wait for the company to fund what you can already start.	1	1263
915	Keep a record for the weeks meaning runs thin. Seeing your contribution making a difference is sustaining now and the difference is most useful on the weeks when it does not feel like much. Keep a record. A simple list of specific examples where your work made a difference added to over the year. On a flat week you reread it and the meaning is restored even if the immediate work in front of you does not yet feel like it adds up.	1	1219
916	Pick a bigger thing you care about and link to it. If you cannot connect your work to anything bigger that is handed to you pick something bigger you actually care about and link the work to it yourself such as the team's success a particular customer's outcome or the kind of professional you want to become. Write one sentence linking your current work to that bigger thing. The link does not need anyone's approval. It changes how the same work feels by Monday.	1	1221
917	Ask your manager once what the purpose actually is. When your manager never links your work to purpose ask once plainly. At your next 1:1 put the question directly: what is the bigger purpose this work is serving? Most managers can answer when asked even if they would not have raised it. The answer might be useful partial or revealing in what is missing and any of those is more useful than working in the dark.	1	1222
918	Track your outcomes and share them at the right moments. When your contribution feels invisible the invisibility is usually a tracking problem rather than a value problem. Track what you produce and the difference it makes monthly. At the right moments performance reviews 1:1s project updates share the tracked outcomes. Tracked contribution is visible. Untracked contribution is felt by you and forgotten by everyone else even when it was genuinely valuable.	1	1223
925	Make the kit list visible. Your equipped state isn't universal. Write a one-page note of the tools accounts and licences you actually use with a line about why each matters. Pass it to anyone new joining your function so they don't lose two weeks chasing what you already have.	1	1233
926	Trace the information that reaches you. You rely on a pipeline most people can't see. Spend twenty minutes listing where each piece of working information comes from the person the dashboard the channel. Share that list with someone newer than you it shortcuts months of guesswork.	1	1234
927	Name the person who shows up for you. Support shows up because someone chose to give it. Send a specific message this week to whoever has stepped in for you naming what they did and what it changed. Then offer the same back when their week gets heavy.	1	1235
928	Quantify what the missing kit costs you. Vague gaps get ignored and specific ones get budget. Note exactly which tool or equipment is missing the tasks it blocks and the hours lost each week. Send that to your manager as a procurement case rather than a complaint decisions need numbers.	1	1237
929	Pick the single biggest information gap and ask directly. Rather than waiting for the system to start working identify the one piece of information that would unblock the most work and approach the person who holds it. One direct request usually beats six weeks of frustration.	1	1238
930	Build a personal go-to list of two or three colleagues. Support isn't a department it's people. Identify the colleagues who actually respond and offer expertise save their names somewhere visible. Reach out to them by name when you need help and offer to return the favour when they're stuck.	1	1239
937	Write your weekly priorities where others can see them. Clarity becomes contagious. If you know what you're working on this week post it in a shared channel or doc. Colleagues who lack the same clarity will start to copy the practice and your manager sees alignment without having to chase it.	1	1249
938	Replay the expectation back in your own words once. Explicit becomes mutual when it survives translation. When your manager sets clear expectations take twenty seconds to summarise what you heard before the meeting ends. You catch any small mismatches early and the habit makes future expectations even sharper.	1	1250
939	Use the written objectives as your own check-in tool not just a record. At the end of each week open the document and tick what moved flag what didn't. Five minutes does it. The objectives become a living instrument for your reflection rather than something gathering dust between reviews.	1	1251
940	Pick your own top three this week then ask your manager to react. Unclear priorities don't get fixed by waiting they get fixed by proposing. Send a quick message naming what you intend to focus on and ask if anything is missing. A yes or a redirect gives you what the silence didn't.	1	1253
941	Write down what you think is expected and confirm in one line. If your manager hasn't made it explicit do it for them. Send a short message listing your assumptions and ask if they're right. Putting your read in writing forces a clean answer and creates a record if priorities later shift.	1	1254
942	Keep a short shift log when expectations change. Patterns are easier to challenge than incidents. Each time priorities move note the date what changed and why if you know. After two weeks you can show your manager the pattern rather than complaining about the latest shift. Patterns demand process answers.	1	1255
949	Say yes to one challenge a quarter that genuinely intimidates you. Regular offers only stay regular if you take them. Pick the next one that scares you slightly and accept it. The discomfort is the signal you're growing into territory worth holding rather than a sign you're underqualified.	1	1265
950	Honour delegated work by being transparent on progress. The trust came at a cost to your manager. Send brief updates without being asked flag risks early and bring decisions back when you're stuck. That pattern earns more delegation the absence of it quietly closes the tap.	1	1266
951	Put yourself forward this month for a specific project not a vague availability. If the door is open walk through it with a precise ask. Name the project the role you want what you bring and what you'll learn. Specificity gets you considered vague availability gets you forgotten.	1	1267
952	Make your interest visible by proposing the work not waiting for the offer. Identify a project or stretch task that would benefit the team write a paragraph on what you'd do and what success looks like and send it to your manager. Offers go to people who've signalled what they'd take.	1	1269
953	Volunteer for the unsexy edges of the interesting work. That's where the door opens. If your manager keeps the headline work look for what supports it. Take the prep the analysis the follow-up. Once you've delivered on the edges twice asking for a piece of the centre becomes a smaller leap.	1	1270
959	Practice candour at a lower stakes level first. With a peer who has earned it. If your manager isn't the right channel build the muscle elsewhere. Share a smaller difficulty with someone you trust and notice what happens. The skill of being honest under pressure is portable when the channel changes.	1	1278
960	Document what gets used against you before deciding your next move. When admission becomes ammunition you need a record more than a strategy. Note specifically what you raised when and how it was used later. That log informs whether to escalate exit or change channel. It also clears your own head.	1	1279
967	Write down one thing working well at the end of each week. Even when nothing dramatic happened. The habit you already have can deepen. A single line answering what worked and why across a year becomes a personal playbook of what you should keep doing and what you should stop second-guessing.	1	1289
968	Volunteer to capture the team's stock-take notes once a month. The ritual lasts longer with a custodian. Many team rituals die because no one owns the writing-down. Offer to keep the brief notes and share them after. You'll see patterns the room misses and the practice quietly outlasts personnel changes.	1	1290
969	Bring one specific 'what worked' example to the next prompted review. Prompts only land when someone gives them something concrete to chew on. Arrive with a worked example from your week rather than a generic answer. The conversation goes deeper others bring their own and the prompt becomes a habit rather than a question.	1	1291
970	Block fifteen minutes on Friday with one question. What worked this week. Reviewing what works requires nothing more than the calendar entry and the question. Sit with the question write three lines close the notebook. Compounding starts the moment the habit becomes a rhythm rather than a one-off intention.	1	1293
971	Bring a single observation to the next team meeting. However brief. You don't need a routine to start one. Drop a sentence about something that worked well this week even unprompted. If it lands once it becomes easier to invite the next person to do the same.	1	1294
972	Reflect for yourself even if no one prompts you. The value is yours regardless of the prompt. Spend ten minutes at week's end on what worked and why. The discipline doesn't need a manager's invitation to compound. The clarity it gives you in the next week makes the practice self-justifying.	1	1295
979	Note the moment you stop noticing the challenge. That's the signal to ask for the next stretch. The right pitch today becomes too easy by next quarter if the work doesn't shift. Track when something starts to feel automatic and bring it to your manager as a cue to raise the bar.	1	1305
980	Tell your manager what the current stretch is teaching you. Calibration is a two-way instrument. Specific feedback about where you're growing and where the gap remains lets them refine the next assignment. Without that signal they're guessing. With it they can dial the difficulty precisely to where you'll grow next.	1	1306
981	Pay attention to which type of work energises you. And ask for more of it. A healthy mix is a starting point but inside that mix some tasks light you up more than others. Note which they are over a fortnight and request more like them. Energy compounds faster than effort.	1	1307
982	Propose a small piece of stretch work attached to your existing role. Don't wait for routine to be relieved attach an extra dimension to it. Suggest learning one new tool owning one new outcome taking one external conversation. Stretch added to the role you have is easier to approve than a new one.	1	1309
983	Calibrate yourself by writing a development plan and walking it through your manager. If calibration isn't happening to you do it for yourself. Draft a one-page plan of what you want to learn this quarter what work would teach it and what success looks like. Plans on paper invite real discussion.	1	1310
984	Track the pattern across a week. Balance is a measurement question first. Note what you did each day and how it felt two words is enough. After a week you can show yourself and your manager the actual ratio of dull to overwhelmed and ask for an explicit fix.	1	1311
991	Bring your distinctive thinking into the next meeting. The easy answer is forming and your angle changes that. A culture that welcomes individuality only stays that way if individuals show up. The unusual angle you'd normally hold back is the contribution. Saying it once trains others that the welcome wasn't theoretical and gives the next person permission to bring something less polished too.	1	1321
992	Tell your manager which difference of yours is currently most useful. Valuing isn't enough on its own. They notice you you notice yourself. Naming the specific strength your difference produces helps them assign work to it and helps you stop discounting what comes naturally. Recognised differences become deployed differences.	1	1322
993	Be the person who welcomes the next newcomer specifically. Belonging is a gift you pass on. The next time someone joins the team or moves into your area reach out by name in their first week. Belonging compounds when those who feel it actively extend it rather than passively assume others will.	1	1323
994	Notice which corner of yourself you suppress most often. That's the test of the culture. Track over a week what you don't say the perspectives you soften the questions you swallow. If the answer is significant the cost of conformity is real and worth weighing against the cost of being seen.	1	1325
995	Find one peer who sees your difference and lean on that relationship. Not every valuing has to come from your manager. A colleague who actually sees what makes you good at the work is a stronger asset than a polite manager. Invest in that relationship it's the one your career will draw on.	1	1326
996	Audit where you spend your work time. Note how you feel after each chunk. Belonging is mostly a series of micro-experiences. After a week of notes you'll know which meetings projects and relationships make you feel at home and which don't. That tells you what to expand and what to gently divest.	1	1327
1003	Track which improvements you've actually implemented and which ones drifted. Implementation has the same enemy that all good intentions face namely the next priority. A short list of changes you made and dates makes the difference visible to yourself. The visibility is what keeps the practice from quietly fading.	1	1337
1004	Bring the next improvement with what you want from your manager already attached. Agreeing improvements works best when the proposal is specific. State the change you want to make what you need from them to enable it and when you'd like to start. They agree faster you start sooner.	1	1338
961	Document the lean process so it survives the next reorganisation. Lean is fragile. Write a short guide to how the work actually flows here the shortcuts the people the decisions. Lean processes drift back to heavy whenever someone new arrives and reinvents them. Your note is the immune system.	1	1281
962	Tell a peer in another function how your governance works. Proportionate is portable when it's articulated. Many teams suffer under excess they assume is mandatory. A fifteen-minute coffee describing how your team handles approvals could quietly free up someone else's week and spread the practice further than any policy paper.	1	1282
963	Bring the next review one specific process that needs trimming even small ones. Reviews lose force when they default to the obvious. Spot something nobody else has flagged the low-grade friction you've stopped noticing and put it on the list. Small process wins keep the review habit alive.	1	1283
964	Time one heavy process from start to finish. Hard numbers shift hard processes. Log how long the steps actually take where the waits are and what value each adds. Send that to whoever owns the process with one suggested cut. Data outperforms complaint even from someone without the authority to change it.	1	1285
965	Map who actually needs to sign off versus who's been added by habit. Excessive sign-offs are usually accumulated rather than designed. Sketch the chain then mark which approvals add genuine risk control and which are just inherited. Take that to the process owner with a leaner draft rather than a complaint.	1	1286
966	Pick the one broken process you can route around without permission. Action beats lobbying. Most teams have at least one workflow that everyone moans about but nobody fixes. Find the smallest workaround that gets your work moving and quietly use it. If it holds others will copy it. That's how broken processes die.	1	1287
973	Pass on what reaches you the same day. The flow only stays fast if you forward it. If you receive something a colleague needs to know send it within hours rather than later. Your speed in passing it on is what makes the next link in the chain trust the chain.	1	1297
974	Confirm receipt on the things that matter. Your manager can't see the value of timely forwards without feedback. A quick 'got it this changes my plan for Tuesday' tells them which information is genuinely useful versus polite noise. They route more of the useful and less of the noise.	1	1298
975	Bookmark the three sources you reach for most and write a one-line guide. Access only stays useful if you can find what you need fast. A short personal index of the dashboards channels and docs you actually use saves you minutes daily and helps anyone new to your role start faster.	1	1299
976	Identify the one decision you're regularly late to and trace the information backwards. Pick the decision where the lag hurts most. Work backwards to find where the delay enters the chain. That diagnosis lets you ask the precise person for an earlier ping rather than a vague complaint about timeliness.	1	1301
977	Subscribe yourself directly to the source where you can. Then bypass the bottleneck cleanly. If certain information flows through your manager and gets stuck ask politely whether you can be cc'd or added to the original channel. A direct feed solves the lag without making the bottleneck personal.	1	1302
978	Make the access gap visible by listing exactly what you don't have. Vague complaints about access go nowhere. Write a short list of the specific systems channels or documents you need what you'd do with each and send it to whoever can grant access. Lists get processed where moans get ignored.	1	1303
985	Pick the role two steps from yours. And start the skills for it not the next. Clear paths reward people who already look like the role two ahead. Identify what that role needs find a way to do a small piece of it now and you're not waiting for the path you're walking it.	1	1313
986	Bring your manager into a specific decision not just the long-term plan. Long-term investment lives in short-term conversations. Ask their view on whether to take the project pursue the qualification accept the secondment. That's where their investment becomes concrete and where you'll find out if it's deep or polite.	1	1314
987	Talk to one of the people you've watched grow. Ask what they actually did. Visible progression is more useful when it's explained. A thirty-minute conversation with someone two years ahead gives you the moves they made that you might miss from the outside. They almost always say yes if asked respectfully.	1	1315
988	Build your own path on paper. The company hasn't drawn one but you can. Sketch the role you want in two years and reverse-engineer the experiences you'd need. Take it to your manager as a discussion starter. A drawn path you can show is harder to ignore than a vague ambition.	1	1317
989	Find a sponsor outside your direct line. Not every champion sits where you report. If your manager isn't invested look around. A senior person in another team who knows your work can do more for your future than a lukewarm direct manager. Make yourself useful and visible to one such person.	1	1318
990	Talk to two people inside the company who do progress. Learn what they share. If a pattern blocks people like you study the exceptions. Coffee with two people who've moved up gives you a hypothesis about what makes the difference. You may copy the move or you may decide the cost isn't worth it.	1	1319
997	Share your tool setup with a colleague struggling with theirs. Well-suited tools spread by demonstration. A fifteen-minute walkthrough of how you use your stack saves them weeks of trial and error. The act of explaining also tightens your own setup you usually spot at least one bit that's still suboptimal.	1	1329
998	Document the integrations that quietly do the heavy lifting. Friction-free isn't accident. When tools work together it's usually because someone set up the right hooks. Note which connections matter so they don't get lost in the next tool migration. That documentation protects the working state from well-meaning replacement.	1	1330
999	Notify IT or your tooling team when something works. Not just when it fails. Fast and reliable gets defended better when usage data is voiced. A short message confirming a tool is performing helps the team prioritising upgrades know what to protect. Negative feedback is loud positive feedback funds renewals.	1	1331
1000	Document the mismatch with a concrete cost. Then send it to whoever owns the tool. Wrong tools fail in specific ways. Write down which tasks they slow by how much and what would fix it. Decisions on procurement get made on evidence complaints rarely move the needle. Provide the evidence.	1	1333
1001	Find the one manual handover that hurts most. Then automate around it personally. If the tools don't talk you can sometimes give them a small voice yourself with a sheet a script a copy-paste shortcut. Solving the worst friction at your own scale beats waiting for a procurement decision.	1	1334
1002	Time the slowdowns for a week and submit a ticket with numbers. Slow and unreliable tools survive on vague frustration. Note the times something failed how long you waited what work was blocked. A ticket with measured impact gets prioritised. A ticket saying it's slow goes to the bottom of the queue.	1	1335
1005	Volunteer to write the team's improvement list and circulate it weekly. Team prioritisation only works if someone keeps the list visible. Take that on quietly for a month. You'll see which changes are progressing which are stalling and you'll gain quiet influence over what the team focuses on next.	1	1339
1006	Pick the smallest improvement you've noticed and implement it before end of day. Improvements that never action don't fail in the noticing they fail in the gap between noticing and starting. Choose one tiny change the file rename the meeting shortened the template tightened and do it now. Confidence builds from doing.	1	1341
1007	Propose one specific change you'll make on your own. Agreement isn't required for everything. If your manager and you can't agree on changes narrow the scope to what's already in your gift. Do it measure it for two weeks then bring the result. Results often unlock the agreement that proposals couldn't.	1	1342
1008	Make one improvement to your own work without waiting for the team. Team-level prioritisation may be stuck but your own habits aren't. Pick one practice in your own workflow worth changing this week and change it. The change models the behaviour for everyone else. Models tend to spread faster than memos do.	1	1343
1009	Close the loop on specific feedback by telling the giver what you changed. Clear feedback loses its power when it isn't acknowledged. A short note saying 'I tried what you suggested here's what happened' makes them more likely to keep being specific. The loop is what keeps the feedback specific over time.	1	1345
1010	Ask for the next piece of actionable feedback before the urgency fades. Acting on what your manager gives you doesn't deplete the supply. Each time you implement a suggestion ask what one thing they'd refine next. The habit builds the muscle on both sides and keeps the feedback practical rather than abstract.	1	1346
1011	Build your own library of the concrete examples you've received. The patterns reveal themselves over time. Save them somewhere private. After six months you'll see what your feedback has been pointing at the thing nobody quite named in one sitting but everyone has been circling. That pattern is your real development direction.	1	1347
1012	Ask for an example whenever feedback feels too abstract to apply. Vague feedback isn't fixed by frustration. The next time you receive a general comment ask 'can you give me a specific moment where this showed up'. Most givers can name one and the example turns the comment into something you can act on.	1	1349
1013	Replay unclear feedback back in your words and ask if you've understood. Don't pretend to follow what you don't. Repeat what you heard name what you'd change as a result and check it lands as intended. The act of repeating forces clarity from both sides without making it a complaint about communication.	1	1350
1014	Ask for the precise moment that prompted the feedback. When examples are missing the situation that triggered it is what you need. A single concrete moment is more useful than ten abstract qualities. People are usually willing to give the moment when asked directly and the conversation moves from judgement to learning the actual behaviour to change.	1	1351
1015	Write down which piece of guidance changed your work most this quarter. Practical guidance only stays practical if you can trace its effect. Note what you tried what shifted what stuck. The record makes you specific the next time you ask for advice and you ask better questions next round.	1	1353
1016	Tell your mentor what you've done with their last suggestion. Before you ask for another. Effective mentoring is reciprocal in effort even when it's one-sided in expertise. The minute you start applying what they offered and reporting back they engage more deeply. The loop is the engine of the relationship.	1	1354
1017	Make your next expert conversation count by sending the question in advance. Reaching experts is a privilege prepare for it. A short message ahead of the meeting that frames the problem and what you've already tried lets them arrive with something specific. You leave with a better answer than a cold conversation produces.	1	1355
1018	Ask for the actual next-step move rather than the principle. When guidance doesn't help you usually got the abstraction and not the action. Ask the giver what they would actually do next in your place tomorrow. The concreteness exposes whether they have a real answer or whether the advice was always at the abstract level.	1	1357
1019	Find a mentor outside your direct line. Mentoring doesn't have to come from your manager. A senior peer in another team or someone external to the company often gives sharper guidance because they aren't entangled in the work. Reach out to one this week and ask for a recurring conversation.	1	1358
1020	Build a small advisory circle of three people in different roles. Don't bet on one source. A peer slightly ahead of you someone in a different function and an external person give you guidance from three angles. Most people say yes to a coffee. Three conversations a quarter beats nobody on speed-dial.	1	1359
1021	Name your top three strengths in writing and notice when they show up. Roles that use strengths can quietly drift. A monthly check on whether the work this week actually drew on what you're best at keeps you honest about whether the fit is being maintained or slowly erased.	1	1361
1022	Tell your manager what the strength-aligned work has produced. The data justifies more. Each time work assigned to your strength produces a result send a short note linking the two. Their pattern recognition gets sharper. The next strength-aligned assignment comes faster and the case for shaping your role grows naturally.	1	1362
1023	Recommend a colleague's strength when you see it in action. Networks of recognition reinforce themselves. The same map that makes your strengths known relies on other people naming each other. Be the one who names someone else's strength to your manager this week. The map strengthens for everyone including you.	1	1363
1024	Identify the one part of your job that does use a strength. Then offer to expand it. Roles that ignore strengths usually still have a sliver of one. Find that sliver deliver beyond expectations and propose taking on more of that type of work. Expansion from a strength is more credible than reinvention.	1	1365
1025	Make your strengths visible by leading on work that requires one. Demonstration outperforms claim when your manager hasn't noticed. Volunteer for something that draws on the strength they've ignored. Deliver then surface it briefly in your next 1:1. Demonstrated strengths are harder to ignore than asserted ones.	1	1366
1026	Write your strengths into your own visible bio. The company gallery the team page your email signature. If nobody knows the gap is partly informational. A two-sentence description of what you're good at placed where colleagues see it changes who comes to you for help. Demand often follows visibility.	1	1367
1027	Pair every flag with an early thought on the cause even if tentative. Welcoming the flag is the culture's part. Sharpening the flag is yours. A thirty-second hypothesis about what's behind the issue makes the welcome more productive and trains you to flag with diagnosis rather than alarm. The pattern lifts everyone's contributions.	1	1369
1028	Tell your manager what their action achieved. Action that gets recognised gets repeated. When they fix something you raised name the impact specifically. They notice fewer problems than you do but they see your acknowledgement clearly. That feedback loop keeps the response time fast on the next issue you flag.	1	1370
1029	Be the one who flags bad news first when it's your area. The protection requires use. Cultures that don't punish messengers stop being such cultures if nobody tests them. The next time you spot something going wrong surface it cleanly and early. You renew the norm and you set the team's pace for honesty.	1	1371
1030	Find one person who will hear the flag and route it via them. Problems still need surfacing. If the open door is closed there are usually side doors. A peer a senior in another team an informal sponsor can carry the flag where you can't. Don't let discouragement become silence.	1	1373
1031	Surface the same issue in writing with a deadline for response. Ignored issues raised in conversation get traction in email. A short note naming the issue the cost and what you'd like decided by when forces a response or makes the silence documented. Either way the problem stops being invisible.	1	1374
1032	Document the flag with date and witnesses before raising it next time. When messengers get blamed the only protection is the paper trail. Note what you raised when and who else heard it. The act of documenting protects you and clarifies whether the issue is worth raising or worth taking elsewhere.	1	1375
1039	Keep your progress notes in a single place. Scattered notes lose their compounding effect. A monthly journal a notes app a single doc all work. The point is that you can look back six months and see the trajectory. That perspective changes how you handle the next plateau which is always temporary.	1	1385
1040	Send your manager a specific 'I now can do X' note quarterly. Perspective deepens when shared. Their view of your progress is partial. Your view is intimate. Combining the two quarterly through a short note makes their feedback richer and gives you a memory record of growth you'd otherwise forget.	1	1386
1041	Volunteer to capture the team's progress notes from your reviews. Joint reviews lose force without a custodian of the narrative. Offer to keep a short running log of what the team has shipped learned and improved. Six months in that log is the morale tool nobody knew you were quietly building.	1	1387
1042	Set a fifteen-minute monthly appointment with yourself. No agenda except looking back. Progress unnoticed feels like none at all. Once a month look at your calendar your inbox your projects from six weeks ago and write three lines on what you've moved. The exercise re-anchors effort to actual movement.	1	1389
1043	Write a private list of what you've moved this month. Your sense of progress doesn't need their permission. Acknowledgement from a manager is welcome. The absence of it doesn't make the movement smaller. Keep your own list. Show it to yourself when the day feels flat. Show it to whoever asks for a CV update.	1	1390
1044	Bring one piece of progress to the next team meeting. The practice has to start somewhere. Even if your team never reviews backwards you can introduce a small dose. Share one thing you or a colleague accomplished recently. If it lands the team starts to do the same. If it doesn't you've still done it.	1	1391
1051	Curate a short list of your three best learning sources and share it. Easy access does not help anyone who does not know what is good. A short list of the platforms you actually use the books you would recommend and the courses that paid off multiplies access for newer colleagues and tightens your own focus too.	1	1401
1052	Plan your learning spend by quarter not by impulse. A budget you have access to is a budget you can squander. A short plan one technical book one role-adjacent course one wildcard exploration makes the spend cumulative rather than scattered. Quarterly intent compounds where moment-to-moment buying disappears into the next thing that catches your eye.	1	1402
1053	Make your unblocking request specific. Your manager's effort is finite. When you need them to clear a path send what you need and why in two lines. Specific asks get fast yeses. Vague asks get parked. Respect the help you're getting by making it easy to give.	1	1403
1054	Find one free source for the topic you most need and start there. If formal resources are hard to reach public ones aren't. A book a podcast a free course a substack are usually a search away. Start the learning while the access problem is being solved. The two don't have to wait on each other.	1	1405
1055	Pool a small budget with two colleagues and split a single resource. No budget at the company level doesn't mean no budget at all. Three people sharing a course or a book costs less per head than coffee. Split the access share the notes. The constraint actually forces the discussion that solo learning skips.	1	1406
1056	Document the access hassle with its time cost. Then send it to whoever can fix it. Friction gets fixed when its cost is named. Note the steps the wait times the workarounds. Send it as a short email to the resource owner. Either the friction reduces or you've made the case for going around it.	1	1407
1063	Be the honest voice in the next leadership session you attend. The openness depends on use. If you sit on a question or observation because someone else might ask the practice gets quieter. The next time you have a moment with a leader ask the unpolished question. Their openness has to be exercised to stay open.	1	1417
1064	Pair every critical observation with a constructive thought. Candour lasts longer when it stays generative. Welcoming criticism is a leader behaviour. Making it useful is yours. Name what isn't working and name what you'd try. The combination keeps the welcome warm and trains you to bring fully-formed contributions rather than half-formed complaints.	1	1418
1065	Test the safety with something modest before raising the bigger thing. No-downside is rarely tested with the easy questions. A medium-weight honest observation builds your sense of the actual range. Once you've calibrated the response you can bring the harder issue with the confidence that the channel can carry it.	1	1419
1066	Frame the challenge as a question not a verdict. Defensive responses spike when leaders feel cornered. 'I've been wondering whether we've considered X' lands better than 'X is wrong'. The substance is the same the entry point is different. You may not change the leader but you keep yourself useful to the conversation.	1	1421
1033	Notice early when capacity tightens. The match doesn't hold automatically. Workload and capacity drift apart in weeks rather than months. A short Friday check on what next week looks like compared to your real bandwidth catches the imbalance early enough to negotiate before it tips into the kind of overload you can't recover from.	1	1377
1034	Bring the balance conversation before the workload tips. Balancing is easier upstream. Don't wait for overload to flag it. Show your manager the loaded next two weeks while there's still time to move things. The trust they place in your judgement deepens when you raise the load early rather than when you collapse under it.	1	1378
1035	Be the one who proposes what to drop. Prioritisation works best with options. When the team gets a new ask arrive at the conversation with one suggestion of what could move to make room. Realistic load comes from a culture of trade-offs. Trade-offs come from someone naming them. Be that person.	1	1379
1036	List everything on your plate and rank by importance. The act of writing it down changes what you negotiate. Overload feels like one big blur. Once it's listed and ranked you can identify the bottom three items and propose dropping or deferring them. The list is what gives your manager a real choice.	1	1381
1037	Reply to the next 'can you also' with a yes-and-here's-what-moves response. Don't refuse and don't absorb in silence. Accept the new work and immediately ask which existing piece should pause to make room. The question forces the trade-off your manager would otherwise avoid making. Most piling stops when the cost becomes visible.	1	1382
1038	Start a personal stop-doing list and run it past your manager. When nothing gets de-prioritised at the team level take the initiative at yours. Write down five things you'd stop doing if it were your call with a one-line cost-benefit for each. Bring it to your 1:1. Most managers approve three.	1	1383
1045	Send a two-line confirmation after each 1:1. The actions sharpen when they're written. End-of-meeting clarity tends to fade by the next morning. A short message restating what you'll do and what they'll do creates a record and a shared expectation. Both halves of the loop start running cleaner.	1	1393
1046	Take responsibility for the shared notes one week. Ownership rotates better than it sits. If both of you capture the practice is robust. If just one does it's a single point of failure. Offer to drive the notes for a month then propose your manager does the next month. Robustness lives in rotation.	1	1394
1047	Make the follow-up loop visible at the next 1:1. Name what they did. Follow-through dies when only one side notices. A simple 'thanks for closing X out' opens the meeting in a way that reinforces the behaviour. Your manager hears the recognition the follow-up muscle stays strong.	1	1395
1048	Ask one question at the end of the next 1:1. What's the single most important thing for me to do this week. Vague endings end when someone forces a point. The question is short the answer is short the clarity is real. Make it your closing question every time and the meetings change shape.	1	1397
1049	Bring the agenda and the notebook yourself next time. If actions never get captured capture them. Write what you discussed what you agreed and send it after. The first time it'll feel like extra work. The third time your manager will have started doing it too. Practice spreads.	1	1398
1050	Bring last meeting's list to the next one and walk through it explicitly. Unfollowed actions die in silence. Open the next 1:1 by reviewing what was agreed and where each item now stands. You aren't checking up on your manager you're giving the agreements somewhere to live between meetings.	1	1399
1057	Document each scope expansion as a milestone. The pattern matters as much as the moment. Each new responsibility added is a marker. Note it with the date and what it represented. The list becomes evidence of trajectory for the next conversation about role or pay and a personal reminder of how far you've come.	1	1409
1058	Show your manager your readiness for the next delegation explicitly. Don't wait for them to spot capacity. Send a short note saying which area you think is ready for transfer to you and what you'd do with it in the first month. The clarity makes the next handover faster.	1	1410
1059	Use the room before someone else does. Room is occupied by those who notice it. Find the area of responsibility nobody quite owns and step into it cleanly. Six weeks later the ownership is yours by default. The room you saw becomes the role you have without a formal change being announced.	1	1411
1060	Propose the next responsibility with a draft transition plan attached. Stuck scope rarely opens because someone realises it should. It opens when an employee makes it easy to expand. Draft what taking on Y would look like what would need to move when you'd be ready. Plans get approved where wishes don't.	1	1413
1061	Earn delegation by handling something small flawlessly first. The trust gap closes from the bottom up. Look for the smallest piece of work your manager hasn't handed over and offer to take it. Deliver it cleanly on time without drama. The next ask becomes easier. Holding-on shrinks when competence is demonstrated repeatedly.	1	1414
1062	Look horizontally if you can't grow vertically. Room sometimes lies sideways. If your current responsibilities can't expand ask about cross-team work a project secondment a guild lead. Growth doesn't only happen upwards. A sideways stretch often opens the upward door six months later by giving you a wider view of the business.	1	1415
1069	Defend the priority list against the next interruption. Clarity has to be enforced. Even with clear priorities new demands appear hourly. The discipline is responding to each with a quick check where does this fit. Most interruptions are smaller than they look. Saying so out loud protects the clear week ahead.	1	1425
1070	Bring your prioritisation question with a starting answer attached. Even helpful managers help more when you don't arrive blank. Say what you'd do if it were your call then ask what they'd change. They sharpen the answer in seconds you build your prioritisation muscle and the conversation takes half the time.	1	1426
1071	Articulate the trade-off you've made out loud. Realistic prioritisation only stays realistic when defended. When you choose A over B say so to your manager your stakeholders your team. The audible trade-off keeps people aware that you can't do everything. That awareness is what protects the realism of next week's demands.	1	1427
1072	Force the ranking by giving everything a number from one to ten. Refuse duplicates. When everything is top priority nothing is. Send your manager your list ranked from one to ten with a note that you'll work through it in order. They'll quickly tell you what's actually first. Numbers force decisions words avoid.	1	1429
1067	Find the channel that does welcome honest views. Every organisation has one. Honesty at the top might be unwelcome but somewhere lower or sideways it lands. A peer group a community of practice a skip-level if it exists. Use that channel for the honesty that isn't safe upstream. Your voice still matters.	1	1422
1068	Document what happened to honest contributions before you make the next one. When candour backfires the pattern matters more than any one incident. Note who said what what the response was what changed for the speaker afterwards. The log helps you decide whether to keep speaking change channels or quietly start planning your exit.	1	1423
1075	Capture each lesson in a single sentence with the date attached. Lessons that stay in your head get diluted by the next ten things you do. A single sentence in a notebook or a doc with the date and one tag is enough. The act of writing the sentence is the act of locking the lesson.	1	1433
1076	Drive the debrief with a specific question. Rather than waiting for it to start. Debriefs that wander stay shallow. Arrive with the one question that genuinely puzzles you about the outcome. The depth shifts immediately. Your manager learns from the debrief too and the practice becomes a real instrument rather than a polite habit.	1	1434
1077	Volunteer to write up the team retro and circulate the lessons. Retros without a written output evaporate by Friday. Take responsibility for capturing the three lessons each retro produces and sending them around. After a year that document is the operational memory of the team. After two it's a training tool.	1	1435
1078	Steal five minutes after each significant piece of work for one lesson. Lessons don't need a half-day workshop they need a notebook and a habit. When something ships ask yourself one question what would you do differently next time before moving to the next task. Five minutes makes the difference between learning and looping.	1	1437
1079	Run your own debrief on paper. The lesson is yours regardless. Don't make your manager the bottleneck on your own learning. After each meaningful outcome write down what worked what didn't what you'd repeat. Bring the best two lessons to your next 1:1 if appropriate. You learn either way the conversation becomes a bonus.	1	1438
1080	Bring one recent mistake to the next team meeting yourself. The pattern breaks when one person breaks it. Repeated mistakes are protected by silence. Name a recent miss of your own briefly without drama. The room learns it's safe to do the same. The team's reviewing muscle starts to develop from a place of shared honesty.	1	1439
1087	Share what your last struggle taught you. The culture is reinforced by the testimony. If your team treats struggles as learning the next person looking at theirs needs to hear someone speak about a previous one. Briefly name what you were stuck on what shifted what you now know. The culture stays alive in stories.	1	1449
1088	Show your manager what you'd take from the coaching. The loop sharpens both of you. After they've coached you through a hard spot send a short note on what landed and what you'll try. They learn how their coaching translates into action. You consolidate the lesson by articulating it. Both halves get stronger over time.	1	1450
1089	Convert one recent setback into a single page someone else can use. The support gave you the lesson the writing-down extends its reach. A short note on what went wrong what you learned what you'd suggest for the next person hitting the same wall multiplies the value of the help you received.	1	1451
1090	Talk to a peer outside your team about a current struggle. Fresh eyes break frozen frames. Frustration is partly the experience of looping inside the same thinking. A thirty-minute conversation with someone outside the immediate work usually unfreezes one assumption. The unfreezing is what turns the struggle from blockage into lesson.	1	1453
1091	Reflect for yourself in writing. The learning isn't gated by your manager's attention. Sit down after a difficult week with three questions what happened what I tried what I'd try next. Your written reflection on a difficult event captures more than most coached conversations do anyway. Make it a habit rather than a reaction.	1	1454
1092	Build a private list of two or three people you'd call when stuck. Support that doesn't exist organisationally has to be assembled personally. Identify the colleagues friends or former managers you'd genuinely reach to when a setback lands. Keep the list short and use it. Recovery is faster when the call list already exists.	1	1455
1099	Raise the smaller concerns publicly. Not just the big ones. Teams that feel safe for concerns can drift into only-the-big-stuff territory. Test the safety with a smaller observation in the next meeting. The practice refreshes the norm and keeps the channel warm for when the bigger thing comes up.	1	1465
1100	Voice dissent when you have a real one. The norm only stays welcoming if used. A team welcoming dissent that nobody uses quietly becomes a team that doesn't. The next time you disagree even mildly say so. You aren't being awkward you're maintaining the norm that gives everyone protection.	1	1466
1101	Tell your manager what the open tone has made possible for you. Open tone is an investment that benefits from acknowledgement. A short message naming a specific thing you've raised because the tone existed helps them see the return. They keep setting the tone the team keeps the freedom.	1	1467
1102	Raise the concern with one trusted teammate first. Alliances precede courage. If raising things to the whole team feels risky find one person who'd hear it. A pre-conversation tests the substance and gives you an ally if you decide to bring it wider. Risky alone often becomes manageable in pairs.	1	1469
1103	Write the difficult thing down and send it before the next meeting. Teams that avoid difficult conversations in the room often handle them better in writing. A short note to the team naming the issue what's at stake what you'd like discussed gives the conversation somewhere to land rather than evaporate.	1	1470
1104	Find a channel outside your manager for the concerns that matter. When discouragement starts at the top of your team the concern still needs somewhere. A peer in another team an internal community an HR contact or simply a private journal until the right moment. Don't let discouragement turn into silence.	1	1471
1111	Use the scheduled reflection well. Arrive with something specific to reflect on. Scheduled time loses value when filled with generalities. Bring a single recent experience worth examining a project a decision a conversation. The depth of your contribution shapes whether your manager keeps scheduling it. Use it well it keeps coming.	1	1481
1112	Capture the team ritual's outputs in writing once a month. Rituals leave value only if the value gets recorded. Take a turn writing up the key reflections from your team's ritual share with the team. The act of capturing makes the ritual harder to skip and easier to point to as worth keeping.	1	1482
1073	Prioritise on your own and announce the order. Silence is the worst input. If your manager won't help decide yourself and tell them what you've decided. Send the list and the order. They either confirm or correct. Either response is more useful than no decision. Working without a ranking is the worst option.	1	1430
1074	Surface the conflict to whoever owns the demands. Focus problems are usually upstream problems. Two stakeholders both wanting your time isn't yours to solve alone. Put both on a message together name the conflict ask them to align. The conversation goes where it needed to go and you stop bearing their lack of coordination.	1	1431
1081	Be the colleague who points to the quieter person when you've spoken twice. Facilitation works best when it isn't only the facilitator's job. After your second contribution ask explicitly what someone else thinks by name. The pattern lifts everyone's contributions and signals that floor time is consciously shared rather than just observed.	1	1441
1082	Use the norm out loud in the next meeting. Norms only stay alive if invoked. Reference the practice explicitly 'I'm conscious we haven't heard from X yet'. Naming the norm reinforces it. The next person finds it easier to do the same. Norms compound when they're spoken rather than just assumed.	1	1442
1083	Thank the chair afterwards for a specific inclusion move you noticed. Facilitation is invisible labour recognised facilitation continues. A short message naming what they did 'I noticed you brought X in twice that mattered' reinforces the behaviour. The chair tightens the next session because they know someone sees the work.	1	1443
1084	Hand the floor to a quieter colleague when you've already spoken. You don't need to be in the chair to redistribute floor time. After your second contribution turn to someone who hasn't spoken and ask their view directly. The pattern interrupts dominance without confronting it and trains the room over weeks.	1	1445
1085	Invite one quieter person into the next meeting personally before it starts. If the inclusion isn't built into the meeting build it into your prep. A short message to a colleague 'I'd love to hear your view on X tomorrow' primes them and gives them a foothold. Inclusion sometimes needs an invitation.	1	1446
1086	Propose lightweight ground rules for the next meeting. Structure can come from any seat. Send a short message to whoever's running it 'could we try a quick round at the start so everyone speaks once'. Most leads accept easily when the suggestion is small. Facilitation doesn't have to be formal to work.	1	1447
1093	Use the development time to write the idea down within twenty-four hours. Ideas given time still need a record to outlast distraction. A short brief the problem the hypothesis the next test captures the thinking before the week absorbs it. That note becomes what you build the next conversation on.	1	1457
1094	Tell your manager what you learned from the last idea they backed. Success or not. Backing carries a quiet cost. Closing the loop with what worked what didn't what surprised you keeps the backing alive. The next idea gets a faster yes because you've shown the previous yes was respected.	1	1458
1095	Bring something half-formed to the next forum. The space loses force without raw material. Forums survive when people use them properly. Don't polish your contribution to death. Bring something genuinely unfinished and let the group sharpen it. The willingness to be half-formed in public is what keeps the forum useful.	1	1459
1096	Write the idea down in fifteen minutes and put it in a queue. No time for ideas usually means no time to capture them and the gap between thought and forgetting is short. A scrappy paragraph in a private doc keeps the idea alive until the calendar opens or the right meeting appears.	1	1461
1097	Bring the next idea with the smallest possible first test attached. Quick shutdowns happen when ideas arrive as big asks. Reframe yours as a two-week experiment with a defined output. Small tests are harder to reject than abstract ideas. Run the test share the result and the conversation changes shape.	1	1462
1098	Build your own short list of ideas worth saving for later. If ideas have nowhere to go give them somewhere personal first. A private list of three to five ideas you keep adding to becomes a portfolio. Pull from it when context shifts or you change role. Ideas saved are ideas eventually delivered.	1	1463
1105	Document how the systems connect. Integrations stay integrated because someone remembers. Sketch the flow of how data moves between your key systems the handoffs the dependencies. Save it where successors can find it. Smooth integration is rarely accidental and rarely permanent. Documentation is what gives it longevity through change.	1	1473
1106	Notice the reliability and thank whoever maintains it. Reliable systems exist because somebody's job is to keep them that way. A short message to the platform team the SRE the IT lead naming the uptime that's enabling your work reminds them the work matters. Recognition is part of the maintenance budget.	1	1474
1107	Capture your no-workaround setup as a quick guide for newer joiners. Workaround-free working is rare and instructional. Spend half an hour writing how you've configured your tools and processes to keep the work flowing. Share with anyone new to your function. You save them weeks and tighten your own setup in the writing.	1	1475
1108	Map the disconnect in three steps. The data the gap the cost. Vague friction is hard to fund a fix for. Note what data lives in System A what's needed in System B and what each manual transfer costs you per week. Send the map to whoever owns the systems. Mapped friction gets prioritised.	1	1477
1109	Time the downtime and log it for a fortnight. Frustration without numbers gets dismissed. Track when systems are down or slow how long you're blocked and what you couldn't do. After two weeks you have a real case for the system team. Numbers move priorities where complaints don't.	1	1478
1110	Pick your most-repeated workaround and spend an hour automating one step. Workarounds are taxes on your week. The biggest one is often automatable with a sheet macro a script a shortcut a saved view. Solving even one shaves hours and proves what's possible. Then make the case for the rest.	1	1479
1117	Use the notice to prepare your team or stakeholders too. The timeliness was a gift to pass on. When you receive early notice of a change identify who else benefits from knowing and tell them. Notice that propagates becomes notice that's worth giving in the first place. Your colleagues remember who got them ready.	1	1489
1118	Tell your manager what the forewarning let you do. The practice is reinforced by feedback. Specific value 'because you told me Monday. I moved X and avoided a clash' strengthens the behaviour. Without that signal they're guessing at whether forewarning matters. With it they keep the early flag coming on the next change.	1	1490
1113	Protect someone else's reflection time the way yours has been protected. Reflection survives because it's defended by colleagues rather than just defended by you. The next time you see a colleague's reflection slot under pressure say something. The protection of the practice is collective. You strengthen what's strengthened you.	1	1483
1114	Book reflection time on your own calendar and make it non-negotiable. If your manager won't make the space make it yourself. Fifteen minutes weekly recurring with a private note for each session. Reflection isn't something they give you it's something you take. The conversation about it can wait until you have evidence of value.	1	1485
1115	Bring one reflection prompt to the next team standup or retro. You don't need a ritual to do reflection in your team you need to start. Drop one question into the next regular meeting 'what's one thing you'd do differently from last week'. Try it twice. If it lands it's a ritual.	1	1486
1116	Reflect in the gaps you already have. The commute the shower the walk. If reflection always gets squeezed from the calendar move it off the calendar. Five minutes thinking on your walk back to your desk after a meeting captures more than no time at all. Reflection doesn't require a slot.	1	1487
1123	Demonstrate the expertise as it builds. The time becomes self-justifying. Building expertise is invisible until it's used. Share what you're learning with the team a brown bag a written summary a contribution in a meeting. Visible expertise justifies continued investment and signals to others where they can call on you.	1	1497
1124	Bring your sponsor a specific request. Not gratitude. Sponsorship works best when used directly. Rather than thanking your manager for the development backing ask them for the next thing an introduction a stretch role a budget for a course. Active sponsorship is renewable. Treat it that way and it lasts longer.	1	1498
1125	Send your mentor a question with stakes. The relationship deepens through real use. Don't waste mentor time on questions you could Google. Bring something you're actually wrestling with that has consequences. The conversation goes deeper their investment grows and you leave with a real answer rather than a polite chat.	1	1499
1126	Find fifteen minutes a day in something you already do. Expertise doesn't require dedicated time so much as it requires deliberate time. Reading a paper at lunch listening to a podcast on a walk taking notes from a project as you go. The compounding of fifteen minutes daily outpaces the dedicated quarter that never arrives.	1	1501
1127	Make your development case in writing then ask for one specific thing. If your manager is not proactive about your development give them an easy yes. A short paragraph naming the capability why it matters to the team and one specific ask such as a course a project or a conversation gets a yes more often than vague hints do.	1	1502
1128	Approach one person you respect and ask for a single conversation. Mentors don't have to be assigned. Most people respond well to a specific request 'I'd like thirty minutes to learn how you do X'. One conversation can repeat. Three repetitions and you have a mentor without anyone calling it that.	1	1503
1135	Be the empathic response someone else needs this week. Empathy as a norm depends on someone being it. When a colleague has a hard day or a public mistake reach out. A short message a coffee a real check-in. The culture refreshes itself through small acts more than through big policies.	1	1513
1136	Tell the leader what their compassion changed for you. Modelled behaviour deepens when it gets named. After a hard meeting where they showed compassion send a short note saying what it meant. Their courage to model it again grows. The pattern others see strengthens too. Recognition is the maintenance budget for culture.	1	1514
1137	Bring your manager your real difficulty. Not the polished version. Support that hasn't been tested is support you don't fully know you have. The next time you're struggling let them see it. Polished problems train them you don't need them. Real ones train them to keep showing up.	1	1515
1138	Take the empathic response into your own peer group. If blame is the climate build a counter-climate locally. Be the colleague who responds with curiosity instead of judgement when someone else's work goes wrong. Two or three people behaving differently in a team can shift what the team feels like to work in.	1	1517
1139	Find compassion outside the senior layer. Leadership isn't the only source of grace. A mentor a former manager a friend a coach a peer all count. When leaders fail to show compassion the absence doesn't have to leave you stranded. Build your own circle of people who will respond like humans.	1	1518
1140	Document the response patterns over time. You need clarity for your next decision. When mistakes are met with coldness consistently you're collecting data about the environment. Note specifically what happened what the response was what changed afterwards. The log informs whether to adjust escalate or quietly start looking. It also calms the noise.	1	1519
1147	Bring a fresh angle to the next scheduled reflection. Regular doesn't mean predictable. The risk of scheduled reflection is that the conversation circles the same points. Arrive with something the team hasn't talked about a quiet observation a forgotten project an external comparison. Schedule plus novelty is what keeps the practice useful.	1	1529
1148	Recommend the habit to someone outside your team. Established habits inside one team become harder to defend when nobody else has them. A short conversation with a peer in another team about what the practice looks like and why it matters spreads it. The habit becomes more durable when more teams have it.	1	1530
1149	Defend the protected time when it comes under pressure. Protected time isn't protected by the calendar it's protected by people. The next time the reflection slot is suggested for an urgent meeting push back politely. Say what gets lost. The slot that survives one challenge survives the next ten more easily.	1	1531
1150	Bring one reflection question to the next team meeting and watch what happens. Teams that don't reflect together often just need someone to start. End the next standup with 'what's something we could be doing better'. Even a quick answer kicks off a habit. Reflection often needs an instigator rather than an agenda.	1	1533
1151	Pair with one colleague for fifteen minutes of reflection a week. Habits start at scale of two. Find one teammate willing to spend a quarter hour reviewing what worked and what didn't. The two-person practice often expands when others see the pair finishing the week with clearer heads than they have.	1	1534
1119	Use the lead time to ask the deeper question. What's the change actually for. Lead time is wasted if it only goes into mechanics. Spend some of it understanding the reasoning. That deeper read makes your preparation sharper and gives you something to share when colleagues are asking what's really happening.	1	1491
1120	Build a small change-readiness habit. The org won't give notice but you can stay ready. Spend ten minutes weekly noticing which parts of your work are exposed to change who controls them what your fallback would be. Surprise changes hurt less when you've already considered the move you'd make in each direction.	1	1493
1121	Ask your manager directly what's on the horizon at every 1:1. If they don't volunteer the forewarning request it. End each meeting with 'is there anything you can tell me about that's coming'. The question normalises the disclosure. They start sharing what they'd otherwise hold back and you get back the lead time.	1	1494
1122	Build your own lead time by watching the signals upstream. Lead time you aren't given can sometimes be detected. Notice what's happening in adjacent teams the conversations leaders are having the projects gaining attention. Patterns surface changes weeks before announcements. A modest amount of attention can replace half the lead time you weren't given.	1	1495
1129	Honour the trust by surfacing risk early. The stakes only stay manageable that way. High-stakes work earns its high stakes from the consequence of slip. Bring problems forward fast rather than late. The manager who trusted you finds out about the risk in time to support rather than too late to do anything but react.	1	1505
1130	Brief your manager on the important work before they ask. Delegation is sustained by visible momentum. A weekly two-line update on what moved what's blocked what you need from them keeps them informed without making them feel they should check up. Their delegation appetite grows when the work is visibly progressing on its own.	1	1506
1131	Treat the ownership seriously by making decisions. Not just choices. Real ownership means deciding things rather than just executing them. When the next call comes up in the area you own decide it. Send the decision and the reasoning to whoever needs to know. Ownership exercised is ownership confirmed and ownership unused is ownership lost.	1	1507
1132	Volunteer for the stake-raising edge of someone else's high-stakes work. If you're not given high stakes find someone who is and offer to help. A small contribution to a high-stakes project teaches you more than full ownership of a low-stakes one. Six months of that and the next assignment looks different.	1	1509
1133	Take on something on the edge of the important work. You don't need it given to you. Look for what supports the important work your manager holds the prep the analysis the follow-up. Take a piece deliver it cleanly do it again. Trust transfers slowly but it does transfer when consistency shows up.	1	1510
1134	Build your case for stakes elsewhere. The company may not be the only place. Volunteer for cross-team work take an external contribution lead something in your community. Build a track record of stakes you can point to. The next career move internal or external comes with the proof you accumulated yourself.	1	1511
1141	Use the path early. Escalation paths atrophy when they sit unused. Don't wait until a blocker becomes a crisis. The next time something genuinely needs escalating use the path while the situation is still small. The system stays healthy through use. Your own confidence in it builds at the same time.	1	1521
1142	Share the unblock with your manager so they see the impact. When your manager clears a blocker send a short note on what the unblocking enabled you to do. Their willingness to keep clearing blockers grows when they see what comes of it. Unblock-and-acknowledge becomes a habit on both sides.	1	1522
1143	Capture support's best moves so you know what to ask for. Fast responses tend to come when you frame requests well. Note what you asked what response you got what worked. Build a personal playbook of how to ask for support. That makes the next response even faster and more accurate.	1	1523
1144	Build your own escalation map by asking around for two days. Routes that don't exist formally often exist informally. Ask three trusted colleagues 'who do you go to when you're blocked'. Their answers usually overlap. The map you build for yourself is more useful than the one HR couldn't draw.	1	1525
1145	Bring the next blocker with a proposed unblock attached. Slow managers move faster when they don't have to think. Frame the blocker name the cost of waiting and propose the action you'd take if you had their authority. Most slow approvals get faster when the work is already done.	1	1526
1146	Build your own list of two or three faster support routes. When formal support is slow side channels often exist. A colleague who knows the system a public Slack channel an external community a knowledge base. Build the list before you need it. Speed comes from having the alternative already mapped.	1	1527
1153	Pass the reasoning down when you make decisions yourself. Reasoning shared at the top depends on reasoning shared at every level. When you make a call explain why to whoever it affects. The practice you've benefited from gets reinforced every time you use it yourself. Cultures of transparency are built by lots of small acts.	1	1537
1154	Ask one further question when your manager walks you through the thinking. Walked-through reasoning sharpens when you engage with it. The next time they explain a decision ask the assumption you'd most want to test. The conversation becomes mutual sharpening. Their thinking benefits too and you learn how decisions actually get made here.	1	1538
1155	Reference a specific decision rationale you found useful. Transparency only stays transparent when used. Bring up in your next conversation 'when you explained why we did X that changed how I approached Y'. The acknowledgement keeps the openness alive. Without that loop leaders aren't sure their explaining is being heard.	1	1539
1156	Ask 'what was the reasoning' when a decision lands without explanation. Hidden reasoning gets exposed by polite questions. The next time a decision arrives without the why ask directly in the appropriate forum. Most leaders can articulate the reasoning if asked. The question normalises the asking. Others start doing it too.	1	1541
1157	Find someone closer to the decision and ask them. If your manager can't explain a decision the explanation lives somewhere. A peer in another team a senior in the relevant function a published memo. Don't accept the explanation gap as permanent. Track down the reasoning from another source. Knowing the why changes how you respond to it.	1	1542
1152	Reflect for yourself in the gaps. The team's habit can wait. If team time isn't protected your own reflection still benefits you. Five minutes after each significant meeting or project to note what you'd repeat or change. Your reflection becomes more visible to the team as your decisions get better and the habit eventually spreads.	1	1535
1159	Articulate the learning explicitly. What was built in needs to stay named. When you finish a task that taught you something say so to a colleague or in a standup. Learning that's built in can drift into invisibility. Naming it keeps the practice conscious and keeps others noticing what they're learning too.	1	1545
1160	Tell your manager what the latest task-with-learning taught you. The loop sharpens both ways. Specific feedback on what landed and what didn't tells them whether the design worked. They adjust the next task accordingly. The whole practice gets sharper because the learning is being measured rather than assumed.	1	1546
1161	Capture the on-the-job lessons in a single running document. Casual learning evaporates faster than formal learning. A running doc one line per lesson makes the implicit explicit. After six months you have a working document of what you've learned by doing. Future you uses it future hires benefit from it.	1	1547
1162	Pick one task this week and treat it as a learning task. If learning needs spare time make a current task the learning. Choose something on your plate set yourself one thing to do differently or notice. The learning happens inside the work. No spare time needed just a different orientation to the same work.	1	1549
1163	Treat your learning as non-optional yourself. Your manager's view is one variable. If they treat learning as optional you don't have to. Set a small commitment one chapter one episode one observation a day. Build the muscle on your own. The results often shift your manager's view faster than the conversation about learning ever would.	1	1550
1164	Find learning inside the tasks you already have. No room often means no reframing. The work you're already doing has lessons buried in it. Spend two minutes after each meaningful task asking what it taught you. The total cost is small the compounding is real. You learn without needing the day to change shape.	1	1551
1171	Be the rally for someone else on the team this week. Teams that rally only stay that way through reciprocity. The next time a colleague is going through a hard patch reach out specifically. A message a coffee an offer to take something off their plate. The rallying you've received gets paid forward.	1	1561
1172	Tell your manager what the backing made possible during the last hard patch. Backing is sustained by acknowledgement. A specific note 'because you stayed close during X. I came out the other side faster' helps them see the value of what they offered. They keep showing up because they know the showing up matters.	1	1562
1173	Use the support fully. Not just reluctantly. People who received real support often hold back from taking all of it. The next time support is offered accept what's offered fully. Half-taken support is half-felt. The full version makes the recovery faster and signals to the support system that it was worth offering.	1	1563
1174	Build a personal support list before the next setback arrives. Don't wait for the next hard patch to figure out who'd help. List the two or three people inside or outside work who'd actually show up. Tell them in advance you might call. The list is the support ready before it's needed.	1	1565
1175	Reach out to support outside your reporting line during a tough patch. If your manager withdraws when you struggle find your support elsewhere. A former manager a mentor a peer in another team. Don't let withdrawal mean isolation. Recovery is faster with support and the source doesn't have to be the obvious one.	1	1566
1176	Build the support that doesn't exist locally one conversation at a time. If support is absent you can construct it slowly. A monthly coffee with a peer who's been through similar a quarterly call with an old colleague an external coaching session. Support assembled from scratch is more durable than support given.	1	1567
1183	Bring something specific to the next forum. The forum stays useful only with material. Forums die when contributions drift to vague observations. Arrive with a specific practice you'd improve a specific tool you'd try a specific friction you've noticed. Specific material gets specific decisions. Vague material gets recorded and forgotten.	1	1577
1184	Champion one improvement from the last retro until it actually lands. Retros surface ideas. Implementation is where most of them die. Pick one improvement from the last retro and quietly own its implementation. Bring an update to the next retro. The team's faith in retros grows when ideas turn into changes.	1	1578
1185	Bring your own improvement to the next prompt. The practice deepens with reciprocity. When your manager prompts don't just react. Arrive with a method improvement you've been thinking about. The prompts become conversations rather than questions. Your manager sees you taking ownership the team sees the practice as collective rather than top-down.	1	1579
1186	Start the conversation yourself with a single observation about how you work. If the team never discusses improvement someone has to begin. Drop one observation into the next regular meeting 'I've noticed we keep hitting X anyone else'. If the conversation starts you've broken the pattern. If not you've still raised it.	1	1581
1187	Run your own personal retro on the team's work. Without team retros. You can still extract lessons. Spend fifteen minutes weekly noting what worked across the team's work what didn't what you'd suggest. Bring two improvements to your next 1:1 with your manager. The practice spreads from there or it strengthens you anyway.	1	1582
1188	Be the one who raises improvement. The absence is sometimes just waiting for an initiator. If your manager never raises it you can. Bring one specific method improvement to your next 1:1 and ask whether the team should discuss it. The conversation either happens or doesn't. Either way you've changed the default.	1	1583
1195	Write the lesson down within twenty-four hours of the learning. Consolidation needs capture. Learning given time still benefits from anchoring. A short note on what you learned where it applies and how you'd use it next makes the lesson stick. The time is the gift. The writing is what turns the gift into a permanent asset.	1	1593
1196	Tell your manager what's now embedded versus what's still settling. The allowance to embed is most useful when its results are reported. After a stretch of learning send a short note on what you can now do and what you're still consolidating. They calibrate the next stretch you confirm what you've absorbed.	1	1594
1158	Build a hypothesis when the thinking stays hidden then test it carefully. When rationale isn't communicated you're left to guess. Form a hypothesis from what you can see the timing the people the constraints. Test it gently in conversation with someone who'd know. Working out the why for yourself develops the muscle the company hasn't.	1	1543
1165	Apply your view. Opportunities seen but unpursued become opportunities lost. Visibility across the organisation isn't the same as taking action. Pick one opportunity that's interested you for more than a month and do something about it a conversation an application an exploration. Visibility is the start. Action is what compounds.	1	1553
1166	Tell your manager what the exposure has changed for you. Wider exposure works best when its impact is named. A short message saying 'the X you introduced me to has shaped my thinking on Y' deepens their investment. They keep opening doors when they see the doors actually got used.	1	1554
1167	Connect two people in your network who'd benefit from meeting. Networks compound when their owners are generous. Find two people you know who don't know each other but should. Make the intro with one sentence each on why they'd value the other. Generous networks attract more opportunities back to you.	1	1555
1168	Spend an hour mapping where else your skills could go. If you see nothing beyond your role the gap is research rather than reality. Look at the company org chart adjacent functions internal vacancies recent moves. An hour usually surfaces three or four paths you didn't know existed. Seeing them is the first move.	1	1557
1169	Build a relationship with one senior person outside your direct line. If you're boxed in by your manager the way out usually runs through someone else. Identify one senior person in a different team whose work interests you. Make a small contribution ask a thoughtful question build slowly. Six months in you have an ally.	1	1558
1170	Spend twenty minutes a week looking at internal moves and external roles. Visibility into opportunities is partly a function of looking. Check the careers page LinkedIn internal Slack channels and conversations with peers at other companies. Twenty minutes a week builds a mental map of what is out there. Opportunities you can see you can act on.	1	1559
1177	Use the no-meeting time well. The dedication needs results to defend itself. If your team protects focus blocks you need to do work in them that wouldn't have happened otherwise. Tackle the genuinely hard thinking that meetings prevent. The output during these blocks is what makes them survive the next reorg.	1	1569
1178	Bring your own ranking to the priorities review. Joint reviews work best with two views. Don't arrive blank. Send your view of priorities the day before then walk through it together. Both your thinking and your manager's gets tested. The review takes half the time and produces a clearer ranking.	1	1570
1179	Defend the blocks when they come under pressure. Blocked time only holds if you hold it. When someone tries to book over your focus time push back politely. Offer an alternative slot. The protection is partly habit. Every time you defend the block future bookers learn to look elsewhere. The discipline compounds.	1	1571
1180	Decline the next meeting where you're not essential and use the time. Back-to-back meetings persist partly because people accept invites by default. The next meeting where your contribution would be minimal decline and explain briefly. Most senders accept. The hour you reclaim is the start of getting focus time back.	1	1573
1181	Send your priorities and workload to your manager weekly without being asked. If they won't review you can make the review one-sided and visible. A Monday morning message naming what you intend to focus on and what you're parking gives them the chance to react. Either they engage or your direction stays clear.	1	1574
1182	Move focus time to the edges of the day. No diary block can protect midday. If the middle of your day is unprotectable work with that fact. Two hours of focus before the calendar opens or two hours after meetings end captures more output than fighting for the middle. Pick edges that fit your rhythm.	1	1575
1189	Use the recognition channel to recognise someone else this week. Channels exist to be used by everyone rather than just by leaders. Send a public note recognising a colleague's specific contribution. The channel stays alive through use. You also strengthen the relationship with the colleague you've named which compounds independently of the recognition itself.	1	1585
1190	Accept the praise without deflecting and tell them what it meant. Praise from a manager loses its effect when waved away. A simple 'thank you that mattered' lands the recognition both for you and for them. Deflected praise teaches your manager not to bother. Accepted praise teaches them to keep noticing.	1	1586
1191	Recognise a peer specifically and publicly this week. Peer recognition cultures are maintained one act at a time. Pick someone whose recent work helped you and name it in a forum others can see a chat a meeting an email. Recognition spreads when individuals supply it rather than when policies announce it.	1	1587
1192	Make your work visible in a Friday note even if recognition is rare. Where good work goes unrecognised the gap is often visibility. A short Friday note to your manager or wider naming what you completed and what shifted because of it creates the moment of recognition that wouldn't have happened spontaneously. Visibility you build gets seen.	1	1589
1193	Tell your manager once specifically what you're proud of recently. Managers who rarely praise sometimes simply don't know what's worth praising. Don't make them guess. Mention at your next 1:1 a piece of work you're particularly pleased with and what it took. You're giving them the data they need to recognise work that would otherwise stay invisible.	1	1590
1194	Start the peer recognition culture yourself with one specific thank you. Cultures of peer recognition start when one person says something genuine out loud. Be that one. This week thank one colleague publicly for something specific they did. One genuine thanks visible to others does more to start the culture than any team-wide policy.	1	1591
1201	Capture the impact in a single sentence for your own record. Seeing impact in the moment is one thing. Remembering it months later is another. A monthly note of one specific impact your work had with the date becomes invaluable when you are job-hunting defending your role or reminding yourself why you bother. Record is what makes impact lasting.	1	1601
1202	Tell your manager what the visibility into outcomes has changed for you. When they show you results the practice gets reinforced by acknowledgement. A specific note 'seeing that result made me think about Y differently' deepens their commitment to closing the loop. They keep sharing because they see it lands. Visibility compounds when its value is named.	1	1602
1197	Use the pace by revisiting one piece of learning a month. Pace that lets learning sink in pays off through deliberate revisitation. Pick one thing you learned a month ago and ask yourself whether you're still using it. The revisit turns initial learning into permanent capability and the practice is what makes the favourable pace worth having.	1	1595
1198	Build a five-minute review into the end of each piece of work. If you're moving on before learning sticks slow the transition. Five minutes between finishing one thing and starting the next spent on what you'd repeat or change captures the lesson before it's overwritten. The pause is small. The retention is large.	1	1597
1199	Capture the lesson now even if there's no time later. If your manager rushes you the rushing doesn't have to mean the lesson is lost. A two-line note immediately after the rushed handover preserves what you can't process now. You can revisit it next weekend. Capture buys you time even when the calendar doesn't.	1	1598
1200	Build the absorption into the work itself. Not adjacent to it. If the pace leaves no time to absorb embed the absorption inside the doing. Narrate to yourself as you work what's this teaching me what would I do differently. The absorbing happens in parallel with the producing. No extra time is required.	1	1599
1207	Say 'I don't know' explicitly this week. The norm stays alive through use. Cultures that accept uncertainty stay that way because people keep using the acceptance. The next time someone asks you something and you're unsure say so plainly. Uncertainty admitted gets you better answers better thinking and better trust than confident guessing.	1	1609
1208	Match the leaders' openness with your own admission. Leaders modelling uncertainty give you permission to do the same. Take the permission. The next time you're uncertain in a meeting say it. Their modelling only sustains a culture if people at every level mirror it. Your admission strengthens what their admission started.	1	1610
1209	Bring your uncertainty as the conversation. Not the obstacle. A safe manager for uncertainty makes the uncertainty itself useful. Walk into your next 1:1 with something you genuinely don't know and want to think through together. The conversation is what their safety was built for. Used that way the safety deepens.	1	1611
1210	Practice 'I'm not sure yet' as the honest answer in small moments. If certainty is the norm you have to perform practice the alternative quietly. Small admissions of uncertainty in low-stakes moments build the habit and the language. Over time others notice the calm of your honesty. The norm shifts partly because you've been steady.	1	1613
1211	Be the one who admits uncertainty in the next forum. The silence breaks somewhere. Cultures where no one senior admits uncertainty stay that way because nobody breaks the pattern. Be the breaker. Admit a genuine uncertainty in your next public conversation. You may pay a small price short-term and the long-term gain is a culture less brittle than the one you found.	1	1614
1212	Document where uncertainty has been judged. You need data for your next decision. Patterns of judgement matter more than incidents. Note who said what when what happened to the person who admitted. The log informs whether to keep speaking up change channels or take the longer view about the environment. Clarity beats lingering unease.	1	1615
1219	Bring one of your reflections to a colleague this week. Talking deepens the lesson. Reflections shared aloud sharpen in ways private ones don't. Pick one observation from your week and discuss it with a trusted colleague. The conversation tests your thinking and builds shared language for what your team is learning together.	1	1625
1220	Use the forum to share something genuinely uncertain. Not polished. Forums for shared learning thrive on rawness. Bring an unfinished thought to the next session rather than a tidy conclusion. Other people's reflections sharpen yours but only if yours is real enough for them to engage with. Polished forums become hollow forums.	1	1626
1221	Be the colleague who invites a less-shared voice into the sharing. Cultures of sharing lessons compound when they're inclusive. The next time a forum is happening notice who hasn't contributed and invite them in. Bring them the question directly. Cultures stay healthy through deliberate invitation rather than by assuming everyone will speak up on their own.	1	1627
1222	Pair with one colleague for a fifteen-minute lesson share each week. If you only reflect alone the sharing has to start with two people. Find one teammate agree a weekly fifteen minutes exchange one lesson each. The two-person practice multiplies the value of your private reflection and often grows when others notice the difference it makes.	1	1629
1223	Bring your reflection to the next regular meeting even without a forum. If a forum doesn't exist the existing meetings can be used. Drop a one-sentence reflection into the next team meeting 'one thing I learned this week was'. It might catch on. If it doesn't you've still introduced the practice. Forums sometimes start that way.	1	1630
1224	Compare lessons with one colleague outside your team. If sharing isn't done inside your team build it outside. A coffee with a peer in another function once a month with one question each on what you've each learned is a forum of two. The lessons exchange across teams and you stop being culturally trapped by yours.	1	1631
1231	Capture the progression in writing each quarter. The trajectory is easier to see written. Progress that's visible to you can still feel vague without a record. A quarterly note of three skills that have visibly progressed with examples makes the trajectory concrete. The record steadies you on flat weeks and feeds future career conversations.	1	1641
1232	Tell your manager what the tracking has helped you do. The practice deepens with feedback. Managers who track growth respond to evidence that the tracking is useful. Send a specific note saying 'because you flagged X six weeks ago. I worked on Y and here's what changed'. The loop strengthens their commitment to keeping track.	1	1642
1233	Articulate the stretch out loud. The role keeps stretching you partly because you keep choosing it. A stretching role is partly a function of how you show up to it. Each month notice which work has stretched you and which has felt automatic. Choose more of the first ask for more of it where appropriate. Stretch sustained is stretch chosen.	1	1643
1234	Identify the one skill you'd most want to stop stalling. Then start on it. Stalled skills usually have one root cause rather than many. Find yours the lack of opportunity the wrong project the missing mentor and address that one thing. One unblock moves the whole skill forward more than five small improvements would.	1	1645
1203	Pass the impact feedback forward to anyone who contributed. Feedback on your impact isn't only about you others made it possible. Share the difference with the colleague who reviewed your draft the peer who unblocked you the team that gave you the data. The recognition compounds across the network and the next collaboration runs faster.	1	1603
1204	Track one outcome of your work end to end for the next quarter. If you never see impact build the line of sight yourself. Pick one piece of your work whose outcome matters and follow it to its destination. Who used it what changed what value emerged. The tracking gives you the impact data the system didn't.	1	1605
1205	Ask your manager for results once. Plainly in writing. Managers who don't share results sometimes need to be asked. Send a short message naming the piece of work and asking what happened with it. Specific questions get specific answers. The asking normalises the sharing and you may train them to volunteer results next time.	1	1606
1206	Build your own feedback loop with whoever uses your work. If feedback on impact doesn't come through formal channels build informal ones. Identify who actually uses what you produce and ask them directly periodically what value it's bringing. The conversations build relationships and give you the data on impact the system isn't providing.	1	1607
1213	Surface obstacles early with what you've already tried. Fast response rewards prepared reports. The next obstacle you hit raise it with the work you've already done included. The faster the response is enabled by your framing the faster the resolution gets habitualised. Fast tackling is partly a function of how the obstacles arrive.	1	1617
1214	Match your manager's pace by escalating clearly and once. Managers who prioritise clearing obstacles do so when they can act decisively. Give them one clear escalation per obstacle with the fact the cost the proposed fix. Avoid multiple reframings. One clean escalation gets one clean answer fast. That pace is partly yours to set.	1	1618
1215	Acknowledge the owner when they fix an issue. Ownership stays clear through gratitude. Clear ownership for fixing issues is rare. When the owner takes action on something you raised thank them specifically and tell them what their action enabled. Owners stay owners when their ownership is recognised. Anonymous fixers quietly disengage.	1	1619
1216	Build your own escalation route by asking three colleagues who they go to. Where formal escalation routes don't exist informal ones often do. Three quick conversations identify the people who actually move things. The map you build for yourself solves the formal gap and gives you a sense of how the org actually works.	1	1621
1217	Send the blocker with the proposed action when your manager is slow. Slow responders speed up when the work is already done. Frame the blocker name the cost propose the action you'd take with their authority. Most slow managers say yes faster when there's nothing left for them to think about. Speed comes from preparation.	1	1622
1218	Take ownership of the fix yourself when no one will. Momentum beats credit. When nobody owns the problem the work doesn't stop being needed. Pick the smallest piece you can fix without authority and fix it. Document what you did. Six months in you've established that you fix things and the next role respects what your current one didn't.	1	1623
1225	Be the genuine listener someone needs this week. Cultures of listening are maintained by people who listen rather than by people who get listened to. The next conversation you have focus fully on the speaker. Ask the follow-up question. Resist the urge to wait for your turn. Listening compounds when individuals supply it consistently.	1	1633
1226	Tell your manager what the full attention has made possible for you. Full attention is rare and worth acknowledging. After a conversation where their attention helped you think through something send a short note saying so. Acknowledgement keeps the practice alive. Their attention shows up again next time because it landed once.	1	1634
1227	Defend the no-talking-over norm when you see it slip. Norms survive through individuals defending them. The next time someone gets talked over in a meeting you're in redirect 'I'd like to hear the rest of what X was saying'. One redirect is enough to remind the room. The norm holds when individuals hold it.	1	1635
1228	Ask one follow-up question even when you're not the one being listened to. Cultures of listening start with someone listening hard. Be that person. The next conversation ask one genuine follow-up question that shows you heard rather than waited. Others tend to copy the energy in the room. Listening is contagious if it's modelled.	1	1637
1229	Send the important thing in writing when conversation isn't working. If your manager is distracted in person switch channels. A short clear written message gets read with more attention than a verbal exchange. Discuss it after they've read. The listening problem is hard to fix. The channel is sometimes easy to change.	1	1638
1230	Surface the talk-over pattern when it costs you something. If you regularly get talked over the cost is real and worth naming. Find a moment to mention it to the colleague involved calmly with one example. Most people don't realise they do it. Naming it once usually changes things and silence makes the pattern permanent.	1	1639
1237	Apply for the next role that fits while the support is real. Active support for internal moves loses value if it's never used. The next internal role that genuinely fits apply. The application teaches you what the bar is it signals you take the support seriously and it builds your reputation as someone serious about their career.	1	1649
1238	Bring your manager into a specific exploration. Openness deserves a use. If your manager is open to new paths give them something concrete to be open about. 'I've been thinking about X function can I shadow someone for a day'. Specific asks deepen their support. Vague exploration ends up as a polite agreement that never happens.	1	1650
1239	Use the pathway visibility to plan one move two years out. Visible pathways are most useful when used for planning. Identify the pathway that interests you most and sketch a two-year plan for moving along it. Take the plan to your manager. Visibility plus planning produces movement. Visibility alone often just produces window-shopping.	1	1651
1240	Network sideways instead of asking permission. If internal moves are quietly discouraged. Build relationships across the company anyway. Coffee with two people in other functions a month is hard to discourage. Eighteen months of that and you've built the optionality the quiet discouragement was trying to prevent.	1	1653
1241	Find a sponsor outside your direct line. The keeper isn't the only voice. If your manager wants to keep you where you are the conversation that gets you out runs through someone else. Identify a senior person whose team you'd want to move to and build a relationship. Six months on your current manager's preference matters less.	1	1654
1235	Track your own growth. Your manager's attention is one input among many. Don't let their lack of tracking become your lack of tracking. Keep a monthly note of what you've moved on. The record is yours regardless of who else notices. It supports your case for a move a raise or a stretch when the time comes.	1	1646
1236	Bring stretch from somewhere else if your role can't deliver it. If the role doesn't stretch you anymore the stretch can come from outside it. A cross-team project a community contribution a personal project an external course. The role becomes one source of growth among several and you stop being trapped by its limits.	1	1647
1242	Map the paths yourself by talking to ten people in different roles. Invisible pathways become visible through conversation. Spend two months having one short coffee a week with someone in a role you're curious about. After ten conversations you have a clearer picture of the org than any internal mobility document would give you. Paths become visible when investigated.	1	1655
703	Be the safe presence someone else needs this week. Psychological safety as a culture depends on every person carrying some of it. The next time a colleague brings a real concern respond with attention rather than deflection. Safety propagates through small consistent behaviours. You strengthen the culture you're benefiting from when you supply it.	1	703
704	Tell your manager what the safety has made possible. A safe environment is sustained by acknowledgement. A specific note saying 'because the team feels safe. I was able to raise X' helps them see the value of the work they've done. They keep maintaining the conditions because they see what those conditions produce.	1	704
705	Use the safety. Don't just appreciate it. Safety to show up fully only matters if you actually show up fully. Bring the unfinished thinking the personal context the honest doubt. Used safety stays alive. Appreciated but unused safety gradually closes back down because nobody is testing whether it's still there.	1	705
706	Identify what specifically puts you on edge and name it for yourself. Being on edge at work has specific triggers even if it feels general. A week of noticing which meetings people or situations spike the unease gives you specific data. The data informs whether to address the trigger build defences or take a longer view about the role.	1	706
707	Build emotional refuelling outside work while you're at work. If your manager creates tension the recovery has to come from somewhere. A short walk between meetings a five-minute breath at your desk a daily ten-minute call with someone you trust. Recovery built into the day means tension doesn't accumulate to the level it otherwise would.	1	707
708	Document what you've been protecting yourself from. You need clarity for your next move. Self-protection at work is a real cost even when invisible. Note what specifically you guard against who triggers it how often. The record clarifies whether the cost is sustainable fixable or a signal to act. Self-protection is information about the environment.	1	708
1243	Be the safe presence someone else needs this week. Psychological safety as a culture depends on every person carrying some of it. The next time a colleague brings a real concern respond with attention rather than deflection. Safety propagates through small consistent behaviours. You strengthen the culture you're benefiting from when you supply it.	1	1657
1244	Tell your manager what the safety has made possible. A safe environment is sustained by acknowledgement. A specific note saying 'because the team feels safe. I was able to raise X' helps them see the value of the work they've done. They keep maintaining the conditions because they see what those conditions produce.	1	1658
1245	Use the safety. Don't just appreciate it. Safety to show up fully only matters if you actually show up fully. Bring the unfinished thinking the personal context the honest doubt. Used safety stays alive. Appreciated but unused safety gradually closes back down because nobody is testing whether it's still there.	1	1659
1246	Identify what specifically puts you on edge and name it for yourself. Being on edge at work has specific triggers even if it feels general. A week of noticing which meetings people or situations spike the unease gives you specific data. The data informs whether to address the trigger build defences or take a longer view about the role.	1	1661
1247	Build emotional refuelling outside work while you're at work. If your manager creates tension the recovery has to come from somewhere. A short walk between meetings a five-minute breath at your desk a daily ten-minute call with someone you trust. Recovery built into the day means tension doesn't accumulate to the level it otherwise would.	1	1662
1248	Document what you've been protecting yourself from. You need clarity for your next move. Self-protection at work is a real cost even when invisible. Note what specifically you guard against who triggers it how often. The record clarifies whether the cost is sustainable fixable or a signal to act. Self-protection is information about the environment.	1	1663
1249	Capture the ways of working so they survive the next change. Ways of working that set you up for great work are usually invisible until they're disrupted. Document them the routines the rhythms the small protocols that make the team productive. The document protects what's working when the next reorganisation arrives as one always does.	1	1665
1250	Tell your manager what the friction-removal has freed up for you. Managers who clear friction deserve a return signal. After they remove an obstacle send a short note on what you were able to do because of it. Their commitment to clearing friction grows when they see the output it enables. The loop strengthens both sides.	1	1666
1251	Be one of the people who keeps improving how you work. The practice needs participants. Continuous improvement isn't done by a department. It's done by individuals who keep noticing what could be better. Pick one practice this month and propose a refinement. The culture of improvement holds because people keep contributing not because it was decreed.	1	1667
1252	Identify the single biggest quality drag in your ways of working. Fix one piece. If the ways of working undermine quality you don't have to fix everything to start. Pick the one practice that most degrades what you ship and propose one change. One real fix proves what's possible. The rest follows more easily once you've shown it can be done.	1	1669
1253	Tackle the friction yourself. One piece at a time. If your manager won't tackle friction you can still tackle it inside your own work. Pick the friction that costs you most and find one workaround one automation one boundary. Quality improves locally. Sometimes the local improvement scales when others notice the difference.	1	1670
1254	Improve one practice in your own work without waiting for the team. Teams that never improve how they work still contain individuals who can. Pick one of your own practices and improve it deliberately this week. The improvement is yours regardless of the team's pace and over time it modelling spreads to others who notice.	1	1671
1255	Convert each reflection into one specific commitment with a date. Reflections that don't convert to commitments stay observations. After each reflection write one sentence 'by Friday I will do X'. The dated commitment is what carries reflection across the gap into action. Without it the insight stays available and stays unused.	1	1673
1256	Bring the next reflection to your manager with one proposed change. If improvements are agreed jointly give the agreement something concrete to land on. Don't bring the reflection bring what you'd change because of it. Most managers say yes faster when the proposal is specific. The agreement happens and the change starts the same week.	1	1674
1257	Volunteer to track the team's improvement actions and circulate progress. Joint prioritisation only works if someone keeps the actions visible. Take that on quietly for a month. The visibility ensures actions don't drift and your manager and team see what's actually progressing. The practice becomes durable through your custodianship.	1	1675
1258	Pick the smallest reflection-driven change and do it before end of week. Reflections that rarely lead to action usually die in the gap between insight and the first move. Make the gap small. Choose one tiny change from your most recent reflection and do it today this week at latest. Action is what closes the loop.	1	1677
1259	Make one change on your own. Agreement isn't required for everything. If you and your manager can't agree on improvements narrow the scope. Pick a change that's fully in your own gift make it and measure the impact over two weeks. Results often unlock the agreement that proposals didn't. Acting alone is often the route to acting together.	1	1678
1260	Action one improvement at your own level. The team's pace is one variable. Teams that never action improvements still contain individuals who can. Pick one improvement your team has spotted but not actioned and implement it within your own work. The change happens. Others sometimes copy. The practice you wanted at team level starts at the individual level you control.	1	1679
\.


--
-- Data for Name: Industries; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Industries" ("Id", "Name") FROM stdin;
1	IT
2	Healthcare
3	Manufacturing
4	Finance
5	Education
\.


--
-- Data for Name: LLMPrompts; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."LLMPrompts" ("Id", "InitialPrompt") FROM stdin;
1	You are a CLOVER ERA and CLOVER FRAMEWORK EXPERT.\\nI have attached data collected from the ERA app over a cycle of questions. Each row corresponds to one feedback entry, with the following columns:\\n- CompanyName: the company providing the feedback.\\n- CloverCategory: the CLOVER element associated with the question (e.g., Communication, Learning, Opportunities, Vulnerability, Enablement, Reflection).\\n- Question: the question asked.\\n- Answer: the user's answer in free text.\\n- Score: the score given (numerical).\\n- Reason: the prepopulated reason chosen by the user, if any.\\n\\nData:\\n
\.


--
-- Data for Name: ModelConfigurationTypes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ModelConfigurationTypes" ("Id", "Name") FROM stdin;
1	Search-model
2	Batch-model
3	Embedding-model
\.


--
-- Data for Name: NumberOfEmployees; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."NumberOfEmployees" ("Id", "Name") FROM stdin;
1	1-10
2	11-50
3	51-100
4	100+
\.


--
-- Data for Name: OpportunityTypes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."OpportunityTypes" ("Id", "Name") FROM stdin;
1	Prospecting
2	Qualification
3	Proposal
4	Negotiation
5	Closed Won
6	Closed Lost
\.


--
-- Data for Name: Plans; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Plans" ("Id", "Name", "Slug", "MaxTeams", "MaxPeoplePerTeam", "MaxPeople", "MonthlyPrice", "AnnualPrice", "PricePerTeamMonthly", "SavingsPercent", "IsCustom", "IsPopular", "RequiresCreditCard", "IsActive", "Features", "TrialDays", "SortOrder", "StripePriceIdMonthly", "StripePriceIdAnnual", "CreatedAt", "UpdatedAt") FROM stdin;
1	Single Team	single-team	1	20	20	295.00	2950.00	295.00	0	f	f	f	t	["Daily anonymous check-ins", "Bi-weekly manager reports", "Monthly team insights", "Micro-actions delivered to employees daily", "CLOVER framework scoring"]	14	1	\N	\N	2026-03-26 14:22:15.585446+00	2026-03-26 14:22:15.585446+00
2	5 Teams	5-teams	5	20	100	1250.00	12500.00	250.00	15	f	f	t	t	["Everything in Single Team", "Organization-level dashboard", "Cross-team pattern visibility", "Priority support"]	14	2	\N	\N	2026-03-26 14:22:15.585446+00	2026-03-26 14:22:15.585446+00
3	10 Teams	10-teams	10	20	200	2100.00	21000.00	210.00	29	f	t	t	t	["Everything in 5 Teams", "Dedicated onboarding", "Executive reporting", "Quarterly business reviews"]	14	3	\N	\N	2026-03-26 14:22:15.585446+00	2026-03-26 14:22:15.585446+00
4	25 Teams	25-teams	25	20	500	4500.00	45000.00	180.00	39	f	f	t	t	["Everything in 10 Teams", "Custom integrations", "Dedicated account manager", "API access"]	14	4	\N	\N	2026-03-26 14:22:15.585446+00	2026-03-26 14:22:15.585446+00
5	26+ Teams	enterprise	0	20	0	0.00	0.00	0.00	0	t	f	f	t	["Everything in 25 Teams", "Enterprise SLA", "Custom deployment", "Multi-region support"]	0	5	\N	\N	2026-03-26 14:22:15.585446+00	2026-03-26 14:22:15.585446+00
\.


--
-- Data for Name: PromptTemplates; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PromptTemplates" ("Id", "Name", "Version", "Description", "SystemPrompt", "Template", "IsActive", "IsDel", "CreatedAt", "CreatedBy", "UpdatedAt", "MetadataJson") FROM stdin;
1	Clover Era Engine Prompt	1	This prompt directs the creation of one negative, data-driven theme for a CLOVER ERA report, highlighting a specific systemic issue within a pillar.\nIt enforces a strict format with a concise headline and explanation that references score gaps and explains the operational impact.	You are writing one theme for a CLOVER ERA engagement report. Your job is to write one negative theme about {pillar}, naming a specific problem pattern that the data reveals.\n\n## Voice Rules\n\n- Direct, evidence-based, coaching-level\n- Short clear sentences\n- Active verbs over -ly adverbs\n- No em dashes\n- No HR buzzwords (leverage, optimise, holistic, synergy, seamless, robust, game-changing)\n- System is the villain, not any individual. Name the process, the default, the structural pattern. Never name a person as the cause.\n- Never quote a FreeText response verbatim. Paraphrase the signal.\n- If you see a `[name]`, `[email]`, `[url]` or `[phone]` token in the FreeText block, that is redacted PII. Do not speculate what it referred to and do not include the token in your output.\n\n## What you are writing\n\nYou are writing exactly ONE theme. Output only:\n1. A headline of 5 to 8 words that names the problem pattern, not just the pillar\n2. A body of 3 to 5 sentences\n\nSentences 1 or 2 MUST reference BOTH the corrected score AND the gap from the 70 target. The body must name a specific behaviour, ReasonText, or FreeText signal. The closing sentence must interpret what the pattern means: the operational or cultural consequence of this gap persisting.\n\n## Pillar to theme: {pillar}\n\nWhy this pillar was chosen for a negative theme:\n{rationale}\n\n## Pillar data\n\n- Corrected score: {corrected_score}\n- Response count: {response_count}\n- Sentiment breakdown: Happy {happy_count}, Indifferent {indifferent_count}, Sad {sad_count}, Angry {angry_count}\n- Status: {status}\n- Gap from 70 target: {gap_from_target}\n\n## ReasonTexts selected (with counts)\n\n{reason_text_block}\n\n## Negative ReasonTexts (Sad or Angry selections)\n\n{negative_reason_block}\n\n## FreeText responses (PII-redacted, paraphrase only, never quote)\n\n{free_text_block}\n\n## Prior period context\n\n{prior_period_context}\n\n## Output format\n\nReturn ONLY this JSON, no preamble, no markdown fences:\n\n{\n  "headline": "string, 5 to 8 words, names the problem pattern",\n  "text": "string, 3 to 5 sentences, references corrected score and gap, closes with consequence"\n}\n\nDo NOT soften this into a positive. This is a negative theme. Name the problem clearly.\n	For Required Result	t	f	2026-04-23 13:34:13.616173+00	\N	2026-04-23 13:40:34.188574+00	{"Temperature":0.7,"MaxTokens":4096}
\.


--
-- Data for Name: SeedingHistories; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."SeedingHistories" ("Id", "MajorVersionNumber", "MinorVersionNumber") FROM stdin;
1	1	0
\.


--
-- Data for Name: TeamDailyQuestions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TeamDailyQuestions" ("Id", "ContentCultureId", "Text") FROM stdin;
1	1	Could you say what your team is aiming for right now?
2	1	This is one of your team's goals. Is it clear how you can contribute to achieving this goal?
3	1	Does your team have the skills the work actually needs?
4	1	Is the work on your team shared out fairly?
5	1	Are you proud of what your team has been delivering?
6	1	When work reaches you, do you have what you need to start?
7	1	Could your team handle something harder than it has now?
8	1	Does your team ever stop and look at how it works?
9	1	Has your team been stable enough to work well together?
10	1	Do you feel like part of your team, or just near it?
11	1	Does your team's goal feel worth the effort it needs?
12	1	This is one of your team's goals. If it got hard, would you want the team to push on?
13	1	Do your own targets pull the same way as the team's?
14	1	This is one of your team's goals. Does it pull the same way as your own targets?
15	1	Do you know how well your team is actually performing?
16	1	Does your team say so when the work isn't good enough?
17	1	When your team agrees to change something, does it stick?
18	1	Can your team talk about what went wrong without blame?
19	1	Is something outside your team making the work harder?
20	1	Are you spending more time coordinating than doing?
21	1	Do you know who decides what on your team?
22	1	When someone here commits to something, does it get done?
23	1	If you were struggling, would someone on your team step in?
24	1	When something goes wrong, does your team recover well?
25	1	How do you feel about what's coming up for your team?
26	1	Would you miss this team if you moved somewhere else?
27	1	If you were choosing again, would you pick this team?
28	1	Could you say what your team is aiming for right now
\.


--
-- Data for Name: TeamQuestionCategories; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TeamQuestionCategories" ("Id", "Name", "Description", "IsActive", "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt", "IsDel") FROM stdin;
0	General	Default category. Questions here are served to every team.	t	\N	\N	2026-08-25 15:27:04.664088+00	2026-08-25 15:27:04.664088+00	f
1	Industrial	Industrial Description	t	50	50	2026-08-25 17:01:43.607365+00	2026-08-25 17:01:43.607365+00	f
\.


--
-- Data for Name: TeamDailyQuestionSpecifications; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TeamDailyQuestionSpecifications" ("Id", "TeamDailyQuestionId", "Variant", "ContentCultureId", "Ordinal", "SubCategory", "TeamElement", "CategoryId") FROM stdin;
1	1	1	1	1	1	1	0
2	2	2	1	1	1	1	0
3	3	1	1	2	4	2	0
4	4	1	1	3	6	3	0
5	5	1	1	4	2	1	0
6	6	1	1	5	5	2	0
7	7	1	1	6	7	3	0
8	8	1	1	7	3	1	0
9	9	1	1	8	4	2	0
10	10	1	1	9	8	3	0
11	11	1	1	10	1	1	0
12	12	2	1	10	1	1	0
13	13	1	1	11	1	1	0
14	14	2	1	11	1	1	0
15	15	1	1	12	2	1	0
16	16	1	1	13	2	1	0
17	17	1	1	14	3	1	0
18	18	1	1	15	3	1	0
19	19	1	1	16	4	2	0
20	20	1	1	17	5	2	0
21	21	1	1	18	5	2	0
22	22	1	1	19	6	3	0
23	23	1	1	20	6	3	0
24	24	1	1	21	7	3	0
25	25	1	1	22	7	3	0
26	26	1	1	23	8	3	0
27	27	1	1	24	8	3	0
28	28	1	1	2	1	1	1
\.


--
-- Data for Name: TeamDailyReasons; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TeamDailyReasons" ("Id", "SpecificationId", "TeamDailyReasonSentimentType", "Text", "RequiresFreeTextFeedback", "ContentCultureId", "TeamState") FROM stdin;
1	1	1	Our goals are set out clearly and often	f	1	2
2	1	1	My manager keeps the goal in front of us	f	1	\N
3	1	1	I can see how my own work feeds the goal	f	1	\N
4	1	1	Other - I'll explain further in the text box	t	1	\N
5	1	2	The goal keeps changing so I've stopped tracking	f	1	\N
6	1	2	My manager has never spelled the goal out	f	1	\N
7	1	2	Nobody explains what success would look like	f	1	\N
8	1	2	Other - I'll explain further in the text box	t	1	\N
9	2	1	Our goals are set out clearly and often	f	1	2
10	2	1	My manager keeps the goal in front of us	f	1	\N
11	2	1	I can see how my own work feeds the goal	f	1	\N
12	2	1	Other - I'll explain further in the text box	t	1	\N
13	2	2	The goal keeps changing so I've stopped tracking	f	1	\N
14	2	2	My manager has never spelled the goal out	f	1	\N
15	2	2	Nobody explains what success would look like	f	1	\N
16	2	2	Other - I'll explain further in the text box	t	1	\N
17	3	1	Between us we cover what the work requires	f	1	\N
18	3	1	My manager brings in help when we're short	f	1	\N
19	3	1	We can get expertise when we hit something new	f	1	\N
20	3	1	Other - I'll explain further in the text box	t	1	\N
21	3	2	We're missing a skill the work clearly needs	f	1	\N
22	3	2	My manager thinks we're better covered than we are	f	1	\N
23	3	2	We keep being asked to do things we can't do well	f	1	\N
24	3	2	Other - I'll explain further in the text box	t	1	\N
25	4	1	Work is distributed evenly across the team	f	1	\N
26	4	1	My manager keeps an eye on who's carrying what	f	1	\N
27	4	1	People take on their share without being asked	f	1	\N
28	4	1	Other - I'll explain further in the text box	t	1	\N
29	4	2	The same people always pick up the slack	f	1	\N
30	4	2	My manager gives the work to whoever says yes	f	1	\N
31	4	2	Some people here just aren't pulling their weight	f	1	\N
32	4	2	Other - I'll explain further in the text box	t	1	\N
33	5	1	Our recent work has been genuinely good	f	1	\N
34	5	1	My manager holds us to a high standard	f	1	\N
35	5	1	We fix things before they reach the customer	f	1	\N
36	5	1	Other - I'll explain further in the text box	t	1	\N
37	5	2	We're shipping work I'm not confident in	f	1	\N
38	5	2	My manager accepts whatever we produce	f	1	\N
39	5	2	We're too rushed to do it properly	f	1	\N
40	5	2	Other - I'll explain further in the text box	t	1	\N
41	6	1	Handovers here come with everything I need	f	1	\N
42	6	1	My manager makes sure the brief is clear before work starts	f	1	\N
43	6	1	I can ask the previous person and get an answer	f	1	\N
44	6	1	Other - I'll explain further in the text box	t	1	\N
45	6	2	Work arrives half-finished and I have to chase	f	1	\N
46	6	2	My manager passes work on without checking it's ready	f	1	\N
47	6	2	Nobody knows what the last person did	f	1	\N
48	6	2	Other - I'll explain further in the text box	t	1	\N
49	7	1	This team has handled tough things before	f	1	\N
50	7	1	My manager backs us to take on difficult work	f	1	\N
51	7	1	We've got the range to take on more	f	1	\N
52	7	1	Other - I'll explain further in the text box	t	1	\N
53	7	2	We're barely coping with what we've got	f	1	\N
54	7	2	My manager keeps handing us more regardless	f	1	\N
55	7	2	We've failed at hard things too often lately	f	1	\N
56	7	2	Other - I'll explain further in the text box	t	1	\N
57	8	1	We hold regular reviews and they're useful	f	1	\N
58	8	1	My manager protects the time for it	f	1	\N
59	8	1	We look at how we work, not just what we did	f	1	\N
60	8	1	Other - I'll explain further in the text box	t	1	\N
61	8	2	We never stop long enough to review anything	f	1	\N
62	8	2	My manager treats reviews as a nice-to-have	f	1	\N
63	8	2	Reviews get cancelled when we're busy	f	1	\N
64	8	2	Other - I'll explain further in the text box	t	1	\N
65	9	1	The team has stayed together long enough to gel	f	1	\N
66	9	1	My manager protects us from constant reshuffling	f	1	\N
67	9	1	New joiners are brought in properly	f	1	\N
68	9	1	Other - I'll explain further in the text box	t	1	\N
69	9	2	People keep joining and leaving	f	1	\N
70	9	2	My manager keeps moving people between teams	f	1	\N
71	9	2	We never get long enough to find our rhythm	f	1	\N
72	9	2	Other - I'll explain further in the text box	t	1	\N
73	10	1	This genuinely feels like a team	f	1	\N
74	10	1	My manager builds the team, not just the output	f	1	\N
75	10	1	I know the people here, not just their roles	f	1	\N
76	10	1	Other - I'll explain further in the text box	t	1	\N
77	10	2	We're a group of people who report to the same person	f	1	\N
78	10	2	My manager only ever talks about the work	f	1	\N
79	10	2	I could leave and nobody would really notice	f	1	\N
80	10	2	Other - I'll explain further in the text box	t	1	\N
81	11	1	The goal matters to people outside our team	f	1	\N
82	11	1	My manager has explained why this matters now	f	1	\N
83	11	1	I'd defend this priority if something tried to displace it	f	1	\N
84	11	1	Other - I'll explain further in the text box	t	1	\N
85	11	2	We're chasing something that won't make a difference	f	1	\N
86	11	2	My manager doesn't seem convinced by it either	f	1	\N
87	11	2	It's the same goal we failed at last time	f	1	\N
88	11	2	Other - I'll explain further in the text box	t	1	\N
89	12	1	The goal matters to people outside our team	f	1	\N
90	12	1	My manager has explained why this matters now	f	1	\N
91	12	1	I'd defend this priority if something tried to displace it	f	1	\N
92	12	1	Other - I'll explain further in the text box	t	1	\N
93	12	2	We're chasing something that won't make a difference	f	1	\N
94	12	2	My manager doesn't seem convinced by it either	f	1	\N
95	12	2	It's the same goal we failed at last time	f	1	\N
96	12	2	Other - I'll explain further in the text box	t	1	\N
97	13	1	My objectives were set from the team's goal	f	1	\N
98	13	1	My manager makes sure they line up	f	1	\N
99	13	1	Helping the team is how I hit my own targets	f	1	\N
100	13	1	Other - I'll explain further in the text box	t	1	\N
101	13	2	My personal targets pull me away from team work	f	1	\N
102	13	2	My manager measures me on something else entirely	f	1	\N
103	13	2	Different parts of the business want different things	f	1	\N
104	13	2	Other - I'll explain further in the text box	t	1	\N
105	14	1	My objectives were set from the team's goal	f	1	\N
106	14	1	My manager makes sure they line up	f	1	\N
107	14	1	Helping the team is how I hit my own targets	f	1	\N
108	14	1	Other - I'll explain further in the text box	t	1	\N
109	14	2	My personal targets pull me away from team work	f	1	\N
110	14	2	My manager measures me on something else entirely	f	1	\N
111	14	2	Different parts of the business want different things	f	1	\N
112	14	2	Other - I'll explain further in the text box	t	1	\N
113	15	1	Our numbers are shared with the whole team	f	1	\N
114	15	1	My manager tells us where we stand	f	1	\N
115	15	1	I can see the effect of my work on the results	f	1	\N
116	15	1	Other - I'll explain further in the text box	t	1	\N
117	15	2	Performance information never reaches us	f	1	\N
118	15	2	My manager only tells us when something is wrong	f	1	\N
119	15	2	Nobody seems to know if we're doing well	f	1	\N
120	15	2	Other - I'll explain further in the text box	t	1	\N
121	16	1	We're honest with each other about quality	f	1	\N
122	16	1	My manager names problems without making it personal	f	1	\N
123	16	1	We'd rather fix it now than defend it later	f	1	\N
124	16	1	Other - I'll explain further in the text box	t	1	\N
125	16	2	We let poor work through to avoid conflict	f	1	\N
126	16	2	My manager avoids the difficult conversation	f	1	\N
127	16	2	The standard has quietly dropped and nobody says	f	1	\N
128	16	2	Other - I'll explain further in the text box	t	1	\N
129	17	1	Changes we agree actually get implemented	f	1	\N
130	17	1	My manager follows up on what we decided	f	1	\N
131	17	1	We revisit changes rather than forgetting them	f	1	\N
132	17	1	Other - I'll explain further in the text box	t	1	\N
133	17	2	We agree things and then nothing happens	f	1	\N
134	17	2	My manager moves on before anything embeds	f	1	\N
135	17	2	We solve the same problem over and over	f	1	\N
136	17	2	Other - I'll explain further in the text box	t	1	\N
137	18	1	We look at what happened, not who did it	f	1	\N
138	18	1	My manager makes it safe to say what failed	f	1	\N
139	18	1	People admit mistakes early here	f	1	\N
140	18	1	Other - I'll explain further in the text box	t	1	\N
141	18	2	Mistakes here turn into a search for who's at fault	f	1	\N
142	18	2	My manager wants a name before a cause	f	1	\N
143	18	2	It's safer to keep quiet when something goes wrong	f	1	\N
144	18	2	Other - I'll explain further in the text box	t	1	\N
145	19	1	Other teams give us what we need on time	f	1	\N
146	19	1	My manager clears obstacles from outside	f	1	\N
147	19	1	We're mostly left to get on with it	f	1	\N
148	19	1	Other - I'll explain further in the text box	t	1	\N
149	19	2	Other teams' delays keep stopping us	f	1	\N
150	19	2	My manager can't shift the blockers above them	f	1	\N
151	19	2	Decisions elsewhere keep changing our work	f	1	\N
152	19	2	Other - I'll explain further in the text box	t	1	\N
153	20	1	Our ways of working actually save time	f	1	\N
154	20	1	My manager keeps meetings to what's needed	f	1	\N
155	20	1	I can get a quick answer without a meeting	f	1	\N
156	20	1	Other - I'll explain further in the text box	t	1	\N
157	20	2	Meetings eat the time I need to do the work	f	1	\N
158	20	2	My manager adds process rather than removing it	f	1	\N
159	20	2	Getting anything agreed takes far too long	f	1	\N
160	20	2	Other - I'll explain further in the text box	t	1	\N
161	21	1	Decision rights here are clear	f	1	\N
162	21	1	My manager is explicit about what I can decide	f	1	\N
163	21	1	We escalate the right things and decide the rest	f	1	\N
164	21	1	Other - I'll explain further in the text box	t	1	\N
165	21	2	Nobody's sure who owns which decisions	f	1	\N
166	21	2	My manager wants to sign off everything	f	1	\N
167	21	2	Decisions get made and then unmade	f	1	\N
168	21	2	Other - I'll explain further in the text box	t	1	\N
169	22	1	People here do what they said they would	f	1	\N
170	22	1	My manager holds people to their commitments	f	1	\N
171	22	1	We follow up on what we agreed	f	1	\N
172	22	1	Other - I'll explain further in the text box	t	1	\N
173	22	2	Things get promised and quietly dropped	f	1	\N
174	22	2	My manager doesn't follow up so nothing lands	f	1	\N
175	22	2	We're all too overloaded to keep promises	f	1	\N
176	22	2	Other - I'll explain further in the text box	t	1	\N
177	23	1	People here cover for each other	f	1	\N
178	23	1	My manager steps in when someone is overloaded	f	1	\N
179	23	1	I could ask for help without it counting against me	f	1	\N
180	23	1	Other - I'll explain further in the text box	t	1	\N
181	23	2	People here are left to sink or swim	f	1	\N
182	23	2	My manager doesn't notice when someone is drowning	f	1	\N
183	23	2	Asking for help looks like weakness here	f	1	\N
184	23	2	Other - I'll explain further in the text box	t	1	\N
185	24	1	We regroup quickly after a setback	f	1	\N
186	24	1	My manager keeps us steady when things go wrong	f	1	\N
187	24	1	Setbacks pull this team together	f	1	\N
188	24	1	Other - I'll explain further in the text box	t	1	\N
189	24	2	One setback and we lose our footing	f	1	\N
190	24	2	My manager panics when things go wrong	f	1	\N
191	24	2	We turn on each other when things go wrong	f	1	\N
192	24	2	Other - I'll explain further in the text box	t	1	\N
193	25	1	What's ahead looks manageable	f	1	\N
194	25	1	My manager has set us up well for it	f	1	\N
195	25	1	We know what's coming and we're ready	f	1	\N
196	25	1	Other - I'll explain further in the text box	t	1	\N
197	25	2	What's coming looks like more than we can take	f	1	\N
198	25	2	My manager hasn't told us what's coming	f	1	\N
199	25	2	Every quarter feels worse than the last	f	1	\N
200	25	2	Other - I'll explain further in the text box	t	1	\N
201	26	1	I'd miss the people here	f	1	\N
202	26	1	My manager is someone I'd want to work with again	f	1	\N
203	26	1	This team has something I'd struggle to replace	f	1	\N
204	26	1	Other - I'll explain further in the text box	t	1	\N
205	26	2	I'd move on without a second thought	f	1	\N
206	26	2	My manager wouldn't notice either way	f	1	\N
207	26	2	There's nothing here I'd miss	f	1	\N
208	26	2	Other - I'll explain further in the text box	t	1	\N
209	27	1	I'd choose this team again	f	1	\N
210	27	1	My manager is a big part of why I'd stay	f	1	\N
211	27	1	The work and the people both work for me	f	1	\N
212	27	1	Other - I'll explain further in the text box	t	1	\N
213	27	2	I'd think twice about choosing this again	f	1	\N
214	27	2	My manager is a reason I'd hesitate	f	1	\N
215	27	2	I ended up here rather than chose it	f	1	\N
216	27	2	Other - I'll explain further in the text box	t	1	\N
217	28	1	Our goals are set out clearly and often	t	1	2
218	28	1	Other - I'll explain further in the text box	t	1	\N
219	28	2	The goal keeps changing so I've stopped tracking	t	1	\N
220	28	2	Other - I'll explain further in the text box	t	1	\N
\.


--
-- Data for Name: TeamDailyMicroActions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TeamDailyMicroActions" ("Id", "TeamDailyReasonId", "ContentCultureId", "Text") FROM stdin;
1	1	1	Write the goal down in your own words. A goal you have heard stated clearly is not the same as one you could restate under pressure, and the gap only shows up when someone asks. Close it now. Write the team goal in one sentence in your own words, then check it against how your manager last described it.
2	2	1	Tell your manager the repetition is landing. Managers restate goals into silence and quietly conclude nobody is listening, which is when they stop. Give them the signal. After the next time your manager restates the goal, reply with one line saying what you took from it.
3	3	1	Say the link out loud to one other person. Seeing how your work feeds the goal is easy to lose when things get busy, and it holds better once you have said it to someone. This week tell one colleague which part of the team goal your current task moves, and ask them the same back.
4	5	1	Ask what the goal is this quarter and write the answer down. When goals keep changing people stop tracking them, which makes the next change harder to absorb. Get one fixed point. Ask your manager what the goal is for this quarter, write it down with the date, and check it again in a month.
5	6	1	Ask the question in a form that is easy to answer. Managers often assume the goal is obvious because it is obvious to them, and a vague ask gets a vague answer. Try a specific one: if we have a great quarter, what will be true that is not true now? Write the answer down.
6	7	1	Propose your own definition and let it be corrected. When nobody defines success, the fastest route to a definition is offering a wrong one. Write one sentence describing what you think a good quarter looks like for the team, send it to your manager, and ask what you have got wrong.
7	9	1	Write the goal down in your own words. A goal you have heard stated clearly is not the same as one you could restate under pressure, and the gap only shows up when someone asks. Close it now. Write the team goal in one sentence in your own words, then check it against how your manager last described it.
8	10	1	Tell your manager the repetition is landing. Managers restate goals into silence and quietly conclude nobody is listening, which is when they stop. Give them the signal. After the next time your manager restates the goal, reply with one line saying what you took from it.
9	11	1	Say the link out loud to one other person. Seeing how your work feeds the goal is easy to lose when things get busy, and it holds better once you have said it to someone. This week tell one colleague which part of the team goal your current task moves, and ask them the same back.
10	13	1	Ask what the goal is this quarter and write the answer down. When goals keep changing people stop tracking them, which makes the next change harder to absorb. Get one fixed point. Ask your manager what the goal is for this quarter, write it down with the date, and check it again in a month.
11	14	1	Ask the question in a form that is easy to answer. Managers often assume the goal is obvious because it is obvious to them, and a vague ask gets a vague answer. Try a specific one: if we have a great quarter, what will be true that is not true now? Write the answer down.
12	15	1	Propose your own definition and let it be corrected. When nobody defines success, the fastest route to a definition is offering a wrong one. Write one sentence describing what you think a good quarter looks like for the team, send it to your manager, and ask what you have got wrong.
13	17	1	Find the skill that rests on one person. Coverage feels fine until the person who covers something is away, and nobody notices the single point of failure in advance. Look now. Write down which skill on your team depends on one person only, and raise it with your manager.
14	18	1	Ask for the help earlier than feels reasonable. A manager who brings in help still needs to know it is required, and most requests arrive after the deadline is already at risk. Move it forward. Flag the gap when you first notice it, not when it starts costing you.
15	19	1	Bring back what you learned so the team keeps it. Outside expertise builds the team only when what came back is shared, otherwise it stays with one person. Pass it on. After the next time you get external help, spend ten minutes explaining it to a colleague.
16	21	1	Name the gap precisely enough to act on. A vague skills gap gets a vague response, and we need more capability names nothing anyone can hire or train. Be exact. Write down the specific task that gets stuck and what someone would need to know to unstick it.
17	22	1	Show the evidence rather than the opinion. A manager who believes the team is covered will not be moved by a differing opinion, but they will be moved by a specific stall. Show one. Point at the piece of work that is late because of the gap.
18	23	1	Ask which of the two matters more. Being asked to do things outside your capability is a prioritisation problem handed down as a delivery problem. Hand it back. Ask your manager which of the competing demands should get the team's real capability.
19	25	1	Check the invisible work is counted too. Even distribution usually covers the visible work, while unglamorous maintenance falls to whoever notices it first. Look at that layer. Note who picks up the unassigned work on your team, and whether it is always the same person.
20	26	1	Tell them when your load changes. A manager watching workload is working from what they can see, and most increases arrive quietly. Update them. Say something when your load changes materially, rather than at the point you cannot cope.
21	27	1	Take the next thing nobody has claimed. A norm of people taking their share holds only while enough people keep supplying it. Supply some. Pick up the next unowned task before it has to be assigned to anyone.
22	29	1	Name the pattern without naming the person. When the same people always absorb the extra, the pattern needs to be visible without becoming an accusation. Describe the shape. Tell your manager how the unassigned work is distributed, not who is failing to take it.
23	30	1	Say no once, with an alternative attached. Work flows to reliable people until they stop being available, and nobody discovers the imbalance until someone declines. Decline once. Say no to one thing and suggest who else could take it.
24	31	1	Make your own contribution visible rather than theirs invisible. Uneven effort is corrosive, and the route that works for you is showing what you carry rather than pointing at what others do not. Show it. Make your workload visible to the team, not just to your manager.
25	33	1	Say what specifically made it good. Pride in recent work fades unless the reason is named, and it went well is not a reason anyone can repeat. Pin it down. Write one sentence on what your team did differently on the last good piece of work, and bring it to your next team meeting.
26	34	1	Ask where the bar is moving next. A manager who holds a high standard is easiest to work with when you know where the bar is going rather than discovering it at review. Ask directly what would make the next piece of work better than the last one.
27	35	1	Record what the catch saved. Catching problems before they reach the customer is invisible work, and invisible work stops being valued. Make it visible once. Next time your team catches something, note what it would have cost and mention it at your next review.
28	37	1	Name the one thing you would not sign off. Discomfort about quality stays vague until someone points at a particular piece of work, and vague discomfort is easy to dismiss. Point at one. Identify the specific deliverable you would not put your name to, and raise that rather than the general feeling.
29	38	1	Ask for the standard to be stated, not raised. Asking a manager to raise the bar sounds like criticism; asking them to state it does not. Use the second. Ask what good looks like for the current piece of work before you start it, and write the answer down.
30	39	1	Offer the trade rather than the complaint. Rushed work continues when the only message upward is that it is rushed, because that names no decision. Name one. Tell your manager what you would cut or delay in order to do the current piece properly, and let them choose.
31	41	1	Write down what a good handover contains. Good handovers usually follow an unwritten pattern that new joiners have to guess at. Make it explicit. Note what a complete handover includes on your team, and share it with the next person who joins.
32	42	1	Ask the clarifying question in front of everyone. A clear brief usually got that way because somebody asked a question, and asking it privately means only you benefit. Ask it in the group, so the answer is shared.
33	43	1	Ask once, in writing, so the answer survives. Answers given in conversation are lost to everyone who was not there, and the same question gets asked again later. Write it down. Put your next handover question somewhere the answer will still be findable next month.
34	45	1	Send one specific request rather than a general complaint. Chasing works better when the ask is precise, because the other person can act without a conversation. Be precise. Reply to the next incomplete handover naming the two things you need in order to proceed.
35	46	1	Propose a short checklist for what gets passed on. Work passed on unchecked is a process gap rather than a personal failing, and processes are easier to change than habits. Propose one. Suggest three things that should be attached to anything handed to the team.
36	47	1	Leave two lines when work leaves you. When nobody knows what the previous person did, the fix starts with your own outgoing work rather than with theirs. Go first. Attach two lines to the next thing you hand on: what you did, and what is left.
37	49	1	Name the last hard thing out loud. Teams forget their own track record quickly, and the evidence only works when it is spoken. Say it. Name one hard thing your team got through in the last year, at your next team meeting.
38	50	1	Ask for the next stretch before it is offered. A manager who backs the team needs to know who wants the harder work, and most people wait to be picked. Put your hand up. Tell your manager what you would want the team to take on next.
39	51	1	Test the range on something small first. Confidence in the team's range is more useful when it has been checked recently rather than assumed. Check it. Take on one thing slightly outside the team's usual scope and see what it exposes.
40	53	1	Say what would have to stop. A team barely coping cannot absorb more, and saying so is only actionable when it comes with what would give. Name it. Tell your manager which current commitment would have to move before anything new lands.
41	54	1	Show the queue rather than describing it. When more work keeps arriving, describing the load rarely changes it, but showing the queue sometimes does. Show it. Put the team's current commitments in one visible list and take it to your manager.
42	55	1	Pick something winnable and finish it. Repeated failure erodes belief faster than anything else, and the repair is evidence rather than encouragement. Build some. Argue for one achievable piece of work the team can complete, and visibly finish it.
43	57	1	Bring one thing you have never raised before. Regular reviews stay useful while new material keeps arriving, and they die when the same three topics recycle. Supply something new. Bring one observation to the next review that you have not said out loud before.
44	58	1	Use the whole slot rather than finishing early. Protected review time gets shortened first by the people in it, and a review that keeps finishing early loses its place in the diary. Fill it. Come to the next one with a question you actually want the team's answer to.
45	59	1	Ask about a habit rather than an event. Reviews drift toward events because they are easier to discuss than habits. Pull it back. In the next review, ask about something the team does every week rather than something that happened once.
46	61	1	Take five minutes at the end of one meeting. A team that never reviews will not adopt a full retrospective, but it will usually tolerate five minutes. Start small. At the end of your next team meeting, ask what one thing the team should do differently next week.
47	62	1	Attach the question to a meeting that already exists. If reviews are optional they will keep being cancelled, so stop needing a separate meeting for them. Attach it. Ask one improvement question in a meeting that is already in the diary.
48	63	1	Shorten it rather than losing it. A review cancelled when things are busy is being cancelled exactly when it is most useful. Protect a smaller version. Propose a ten-minute version that survives the busy weeks rather than an hour that does not.
49	65	1	Write down what the team has learned to do well. A stable team accumulates shared understanding nobody has ever articulated, and it leaves with the first person out of the door. Capture some. Note two things your team does well because you have worked together a while.
50	66	1	Tell them the stability is doing something. Managers who defend team stability take a cost for it and rarely hear that it worked. Give them the argument. Tell your manager one thing the team can do now that it could not a year ago.
51	67	1	Offer to be the next joiner's first contact. Good onboarding runs on individuals volunteering, and it degrades quietly when nobody does. Volunteer. Tell your manager you will be the first point of contact for the next person who joins.
52	69	1	Write down what the team knows before it walks out. Turnover erases shared knowledge that was never written anywhere, and each departure costs more than the last. Capture some now. Write down the three things a new person always has to be told, and keep the list.
53	70	1	Ask what the reshuffles are trying to solve. Frequent moves usually have a reason that has never been explained, and knowing it changes whether they feel arbitrary. Ask. Find out what problem the moves are solving, and whether the team could solve it another way.
54	71	1	Build one routine that survives the changes. When a team never settles, the thing to protect is a single stable practice rather than the whole way of working. Pick one. Choose one meeting or ritual and keep it constant through the next change.
55	73	1	Do one thing that makes it true for someone else. Belonging is produced by people, and it holds while enough of them keep producing it. Contribute. Include one colleague this week in something they would otherwise have missed.
56	74	1	Tell them which thing worked. Managers investing in the team rarely find out which efforts landed, so they repeat the wrong ones. Tell them. Name the one thing they did that actually made this feel like a team.
57	75	1	Learn one thing about whoever you know least. Knowing your colleagues is uneven, and the gap is usually with whoever joined most recently or sits furthest away. Close one. Have one non-work conversation this week with the person you know least.
58	77	1	Find one thing to do jointly. A group becomes a team through shared work rather than shared meetings. Create some. Find one piece of work you could do with a colleague rather than beside them.
59	78	1	Ask a non-work question first. When every conversation is about work, someone has to change the pattern and it does not have to be the manager. Change it. Open your next one-to-one with something that is not a status update.
60	79	1	Test it rather than believing it. The feeling that you would not be missed is common and usually wrong, and it gets stronger when it goes unexamined. Test it lightly. Ask a colleague what they would find hardest if you were away for a month.
61	81	1	Find out who notices when you deliver. A goal that matters outside the team motivates more when you know specifically who it lands on rather than just that someone does. Ask your manager or a colleague in another function who feels it when your team delivers this, and what changes for them.
62	82	1	Pass the reasoning on to someone who missed it. Why a goal matters now is the part most likely to get lost between the people who heard the explanation and the people who did not. Be the relay. Tell one teammate the reason your manager gave, in your own words, this week.
63	83	1	Say so before the displacement happens. Priorities get displaced quietly, by a new request that seems small, and defending one after the fact is much harder. Say it early. Next time your team discusses workload, name this goal as the thing you would protect and see whether others agree.
64	85	1	Ask what happens if the goal is missed. A goal that feels pointless usually has a consequence nobody has stated, or genuinely has none, and either answer is useful. Ask your manager plainly what changes if the team misses this, and what changes if it hits it early.
65	86	1	Ask what they would change about it. A manager who is not convinced by a goal is often carrying it from above and has views they have not aired. Invite them. Ask what they would change if the goal were entirely theirs to set, and listen for what is actually bothering them.
66	87	1	Ask what is different this time and get a real answer. Repeating a failed goal without naming what changed is the fastest way to lose a team's commitment. Get the difference stated. Ask what is different about this attempt — resource, approach, timing — and what happens if the answer is nothing.
67	89	1	Find out who notices when you deliver. A goal that matters outside the team motivates more when you know specifically who it lands on rather than just that someone does. Ask your manager or a colleague in another function who feels it when your team delivers this, and what changes for them.
68	90	1	Pass the reasoning on to someone who missed it. Why a goal matters now is the part most likely to get lost between the people who heard the explanation and the people who did not. Be the relay. Tell one teammate the reason your manager gave, in your own words, this week.
69	91	1	Say so before the displacement happens. Priorities get displaced quietly, by a new request that seems small, and defending one after the fact is much harder. Say it early. Next time your team discusses workload, name this goal as the thing you would protect and see whether others agree.
70	93	1	Ask what happens if the goal is missed. A goal that feels pointless usually has a consequence nobody has stated, or genuinely has none, and either answer is useful. Ask your manager plainly what changes if the team misses this, and what changes if it hits it early.
71	94	1	Ask what they would change about it. A manager who is not convinced by a goal is often carrying it from above and has views they have not aired. Invite them. Ask what they would change if the goal were entirely theirs to set, and listen for what is actually bothering them.
72	95	1	Ask what is different this time and get a real answer. Repeating a failed goal without naming what changed is the fastest way to lose a team's commitment. Get the difference stated. Ask what is different about this attempt — resource, approach, timing — and what happens if the answer is nothing.
73	97	1	Check the link still holds after the last change. Objectives set from a team goal drift apart when either one is revised, and nobody usually re-checks. Do it now. Put your objectives next to the team goal, mark any that no longer clearly serve it, and raise those at your next one-to-one.
98	130	1	Report before you are asked. A manager who follows up spends effort chasing, and that effort determines how many changes they are willing to sponsor. Reduce it. Report on your part of the last agreed change before your manager asks about it.
74	98	1	Bring the conflicts to your manager rather than absorbing them. A manager who aligns objectives can only fix the misalignments they hear about, and most people quietly absorb them instead. Name one. If any part of your workload pulls against the team goal, say so at your next one-to-one.
75	99	1	Make the overlap visible to your teammates. When helping the team is how you hit your own targets, saying so removes any suspicion that help comes with a catch. Say it plainly once. Tell a colleague which of your targets their work helps you meet, and offer the same in return.
76	101	1	Put the conflict in writing to your manager. A conflict between personal targets and team goals is a structural problem, not a motivation problem, and it will not resolve by trying harder. Escalate it as a fact. Write two lines naming the target and the team goal it competes with, and ask which wins.
77	102	1	Ask which one to drop when they collide. Being measured on something other than the team goal means a collision is coming, and the worst time to resolve it is mid-collision. Pre-agree it. Ask your manager now which takes priority when the two conflict, and note the answer.
78	103	1	Get the two asks into one conversation. When different parts of the business want different things, relaying between them is how the conflict stays invisible and lands on you. Stop relaying. Ask your manager to get both parties in one conversation, or to state the priority on their behalf.
79	105	1	Check the link still holds after the last change. Objectives set from a team goal drift apart when either one is revised, and nobody usually re-checks. Do it now. Put your objectives next to the team goal, mark any that no longer clearly serve it, and raise those at your next one-to-one.
80	106	1	Bring the conflicts to your manager rather than absorbing them. A manager who aligns objectives can only fix the misalignments they hear about, and most people quietly absorb them instead. Name one. If any part of your workload pulls against the team goal, say so at your next one-to-one.
81	107	1	Make the overlap visible to your teammates. When helping the team is how you hit your own targets, saying so removes any suspicion that help comes with a catch. Say it plainly once. Tell a colleague which of your targets their work helps you meet, and offer the same in return.
82	109	1	Put the conflict in writing to your manager. A conflict between personal targets and team goals is a structural problem, not a motivation problem, and it will not resolve by trying harder. Escalate it as a fact. Write two lines naming the target and the team goal it competes with, and ask which wins.
83	110	1	Ask which one to drop when they collide. Being measured on something other than the team goal means a collision is coming, and the worst time to resolve it is mid-collision. Pre-agree it. Ask your manager now which takes priority when the two conflict, and note the answer.
84	111	1	Get the two asks into one conversation. When different parts of the business want different things, relaying between them is how the conflict stays invisible and lands on you. Stop relaying. Ask your manager to get both parties in one conversation, or to state the priority on their behalf.
85	113	1	Form your own view before hearing the interpretation. Shared numbers only build judgement if you read them yourself first. Try it once. Before the next team review, look at the numbers and write down what you think they say, then compare that with what gets said in the room.
86	114	1	Ask what the number was last time. Knowing where you stand is far more useful with a direction attached, and managers often give the level without the trend. Ask for it. Next time your manager shares performance, ask what it was the period before.
87	115	1	Check whether your read matches the data. Seeing your own effect is easy to get wrong in both directions, and the correction is cheap. Take one piece of work you believe moved the numbers and ask your manager whether it actually showed up.
88	117	1	Ask for one number, not a report. Asking for performance visibility usually produces a promise of a report that never arrives. Ask smaller. Pick the single number that would tell you most about how the team is doing, and ask for that one at whatever frequency is easy.
89	118	1	Ask what good looked like last month. When you only hear about problems you lose the ability to tell a bad month from a normal one. Get the other half. Ask your manager to name one thing that went well last month and why it counted.
90	119	1	Propose a measure and see who objects. When nobody knows how the team is doing, the fastest way to find a measure is to suggest a bad one. Suggest one. Put forward the number you think best reflects the team's work and let people argue it into a better one.
91	121	1	Go first on your own work. Honesty about quality holds while people apply it to themselves as readily as to others. Keep supplying it. In your next team review, name something in your own recent work you would do differently, before commenting on anyone else's.
92	122	1	Borrow the phrasing when it is your turn. A manager who separates the work from the person is modelling something you can reuse, and most people never consciously borrow it. Notice how they phrase it next time, and use the same framing when you raise something.
93	123	1	Make the early flag cheap for whoever raises it. Teams that fix things early depend on someone being willing to speak first. Lower the cost. Next time a colleague flags a problem early, say out loud that raising it was the right call.
94	125	1	Raise it as a question rather than a verdict. Poor work passes because the honest version sounds like an attack and nobody wants the argument. Change the form. Ask what would make this stronger rather than saying it is not good enough, and see whether the conversation opens.
95	126	1	Take one specific issue to them privately. A manager who avoids difficult conversations in the group will often have them one to one. Use that route. Take a single specific quality issue to them privately, with what you would do about it.
96	127	1	Compare against something from a year ago. A standard that drops slowly is invisible because every step looks like the last one. Break the frame. Find something the team produced a year ago, put it next to something recent, and bring the comparison to your next review.
97	129	1	Check the last one is still running. Implemented changes decay quietly, and the team usually finds out when the old problem returns. Check early. Pick the last change your team agreed and verify this week that it is still happening.
99	131	1	Ask whether the change is still worth keeping. Revisiting a change is more useful when it can be dropped as well as continued, otherwise practices accumulate forever. Ask the harder question. At the next review, ask whether an existing practice should stop.
100	133	1	Attach one name and one date before the meeting ends. Agreements without an owner and a date are decisions in name only, and everyone leaves assuming someone else has it. Fix the mechanics. Before the next meeting ends, ask who is doing the agreed thing and by when.
101	134	1	Ask for one change to be held open. When a manager moves on quickly, changes never get long enough to embed. Slow one down deliberately. Ask that a single change stays on the agenda for the next three meetings before anything new is added.
102	135	1	Write down what was tried last time. Recurring problems recur because each attempt starts from scratch with no memory of the last one. Give it a memory. Keep one note of what the team has already tried on the recurring problem, and bring it out next time.
103	137	1	Volunteer your own part first. Blame-free reviews hold while confident and senior people go first on their own mistakes. Take a turn. Next time something goes wrong, name your own contribution before anyone else's comes up.
104	138	1	Give them something real to work with. A manager who makes failure discussable needs real failures brought to them, or the safety goes untested and quietly lapses. Use it. Bring one thing that did not work to your next one-to-one.
105	139	1	Say what you learned, not just what happened. Early admission is more useful to the team when it arrives with the lesson attached rather than as a confession. Add the second half. Next time you flag a mistake, add one line on what you would do differently.
106	141	1	Describe the sequence rather than the decision. When mistakes become a search for fault, the way in is to shift the discussion from the choice to the chain of events. Change the frame. Next time, walk through what happened in order and let the causes surface on their own.
107	142	1	Bring the fix at the same time as the problem. When a manager reaches for a name first, arriving with a cause and a proposed fix gives them somewhere else to go. Give them the alternative. Do not report a problem to them without one suggested next step.
108	143	1	Tell one person you trust before it grows. When staying quiet feels safer, the real risk is that the problem is discovered later and larger. Reduce the exposure. Tell one colleague you trust early, so the problem stops being yours alone to carry.
109	145	1	Say thank you specifically enough to be repeated. Reliable input from another team is a habit held in place by individuals, and habits fade without acknowledgement. Reinforce it. Tell the person in the other team what their on-time delivery let your team do.
110	146	1	Report the obstacle rather than working around it. A manager who clears external obstacles can only clear the ones they hear about, and quietly working around them hides the pattern. Report the next one, even if you have already found a way round it.
111	147	1	Use the space rather than letting it fill. Being left alone is an asset that gets consumed by whatever arrives, unless you deliberately spend it. Spend some. Pick one improvement your team has been putting off and use the quiet period on it.
112	149	1	Track the delay so the pattern is visible. A single delay from another team is an incident; a repeated one is a structural problem, but only if someone is counting. Count. Note each time your team waits on the same source and for how long, then show the list to your manager.
113	150	1	Ask what would need to be true to move it. A blocker your manager cannot shift is usually stuck on something specific rather than being immovable. Find out what. Ask what would have to change for it to move, and whether anything you do could help.
114	151	1	Find out when the decisions actually get made. When outside decisions keep changing your work, the useful move is earlier warning rather than fewer changes. Get closer to the source. Find out where the decision is made and ask to see the agenda rather than the outcome.
115	153	1	Notice which practice is doing the work. Ways of working that save time contain one or two practices doing most of the lifting, and the rest is decoration. Find them. Identify which practice you would keep if you could only keep one, and say so at the next review.
116	154	1	Decline the one you add nothing to. A manager keeping meetings lean needs the same discipline from everyone in them. Contribute. Identify one recurring meeting where you neither give nor get anything, and ask whether you need to be there.
117	155	1	Ask in the form that is quickest to answer. Quick answers stay quick when the question is framed so it can be answered in a line. Frame it well. Next time you need a decision, give the options and your recommendation rather than the background.
118	157	1	Count the hours, then drop one meeting. Meetings accumulate because each looks individually reasonable and nobody adds them up. Add them up. Count your recurring meeting hours this week, pick the least useful one, and ask the organiser whether you need to attend.
119	158	1	Propose a removal whenever an addition is proposed. Process accumulates because additions get suggested and removals never do. Suggest the removal. Next time a new step is added, ask which existing step it replaces.
120	159	1	Find out who actually needs to agree. Slow agreement is usually caused by too many people being asked rather than by anyone being slow. Narrow it. Establish the smallest set of people who genuinely need to sign off, and ask the rest for comment instead.
121	161	1	Test the boundary you have never tested. Clear decision rights stay clear until an unusual case arrives, which is exactly when the ambiguity costs most. Test one now. Identify a decision you are not sure you own, and confirm it before you need to make it.
122	162	1	Use the full width of what you were given. Explicit authority goes unused when people check anyway out of habit, and unused authority gets quietly withdrawn. Use it. Make the next decision inside your remit without checking, and tell your manager afterwards.
123	163	1	Notice what you escalated that you could have decided. Escalation habits drift upward gradually, and nobody notices they are checking more than they used to. Review one. Look at the last thing you escalated and ask whether it needed to go up.
124	165	1	Write your own version down and circulate it. Unclear decision rights persist because writing them down looks like a big exercise. Make it small. Write your understanding of who decides what, send it to the team, and let the corrections define it.
125	166	1	Offer a threshold rather than asking for freedom. Asking a manager to step back sounds like a challenge; proposing a threshold does not. Propose one. Suggest a value or type of decision below which you proceed and simply inform them.
126	167	1	Ask who else needs to be in the room first. Decisions get reversed when someone with a say was not present, and repeating the meeting is the expensive fix. Prevent it. Before the next decision meeting, ask who could overturn this and whether they should be there.
127	169	1	Say when you cannot, as early as you can. Reliability is a team property held up by early warnings as much as by delivery. Supply the warning. The moment you know something will slip, say so rather than waiting to see whether you can recover it.
128	170	1	Say out loud what you have taken on. Commitments made in conversation are remembered differently by everyone in the room. Check yours. Before the next meeting ends, state what you are taking away from it.
129	171	1	Close the loop even when the answer is no. Follow-up works when it covers the things that did not happen as well as the things that did. Report both. Next time you cannot deliver something you agreed, close it explicitly rather than letting it lapse.
130	173	1	Write down who agreed to what and share it. Quietly dropped commitments survive because there is no record to compare against. Create one. Note the agreements at the end of your next team meeting and circulate them.
131	174	1	Follow up on your own part visibly. When nothing gets followed up, the fastest change is one person reporting on themselves without being asked. Be that person. Report on what you agreed at the next meeting, whether or not anyone asks.
132	175	1	Commit to less and deliver it. Overload makes broken promises normal, and the cure is fewer commitments rather than more effort. Cut one. Next time you are asked, agree to a smaller piece you can actually finish.
133	177	1	Offer before you are asked. Mutual cover is a habit that decays if everyone waits to be asked. Keep it alive. Notice one colleague under pressure this week and offer something specific rather than a general offer of help.
134	178	1	Flag the pressure before it peaks. A manager who steps in still needs to know, and by the time it is obvious the support arrives late. Give them warning. Say something when the load starts building, not when it has already cost you.
135	179	1	Ask for something small to keep the route open. Help that is available but never used quietly becomes unavailable, because nobody is sure any more. Test it lightly. Ask a colleague for something small this week.
136	181	1	Build one mutual arrangement with a colleague. When a team leaves people to cope alone, the fix is not the team but one person. Find one. Agree with a single colleague that you will each flag pressure to the other, explicitly and both ways.
137	182	1	State the position in numbers rather than feelings. Managers who miss overload often respond to a workload count when they did not respond to a description of stress. Change the format. Show what you are carrying as a list, with what would have to move.
138	183	1	Frame it as a check rather than a rescue. Where asking for help carries a cost, the framing does most of the damage. Reframe it. Ask a colleague for a sense-check on your approach rather than for help with the work.
139	185	1	Say what made the recovery work. Teams that recover well rarely examine how, so the capability stays accidental. Make it deliberate. After the next recovery, name the one thing that made it work and repeat it next time.
140	186	1	Take one thing off them during the next one. A manager holding a team steady is absorbing a lot, and that capacity is finite. Reduce the load. During the next setback, take one thing off their plate without being asked.
141	187	1	Say so while it is happening. A team that pulls together under pressure benefits from someone naming it in the moment. Name it. During the next difficult stretch, say out loud that the team is handling it well.
142	189	1	Get the team to a small win quickly. When a setback destabilises a team, the fastest stabiliser is completing something visible. Find one. Identify the smallest thing the team can finish this week, and get it finished.
143	190	1	Bring options rather than the problem. A manager who reacts badly to problems reacts differently to a problem that arrives with two possible responses. Change what you bring. Do not take them a setback without at least one proposed next step.
144	191	1	Move the conversation to the sequence of events. Teams turn on each other when the discussion is about decisions rather than about what happened. Change the frame. Walk through the order of events and let the cause emerge without a name attached.
145	193	1	Check the plan against the quiet risk. Work that looks manageable is where teams under-prepare, because nobody is looking for the problem. Look once. Name the one thing that would make the next piece harder than expected, and raise it early.
146	194	1	Ask what they are still worried about. A manager who has prepared the team well usually has a residual concern they have not shared. Ask for it. Find out what part of what is coming still worries them, and take one piece of it.
147	195	1	Confirm everyone knows, not just you. Readiness is usually less evenly spread than the confident people assume. Check the spread. Ask one quieter colleague what they think is coming next, and compare it with your own picture.
148	197	1	Separate what is committed from what is assumed. Work ahead looks worse in the aggregate than it is once separated into what is agreed and what is merely expected. Separate it. List what is genuinely committed for the next month and see what remains.
149	198	1	Ask for the next month, not the strategy. Managers often withhold plans because they feel incomplete, and a smaller question gets a real answer. Ask smaller. Ask what the team should expect in the next four weeks.
150	199	1	Compare the last two quarters properly. A sense of escalating pressure is sometimes accurate and sometimes just recency. Test it. Write down what the team delivered in each of the last two quarters and compare the actual load.
151	201	1	Tell one of them. Attachment to colleagues is rarely said out loud, and saying it strengthens the thing it describes. Say it once. Tell one colleague specifically what you value about working with them.
152	202	1	Say so where it counts. Managers get very little feedback of this kind, and the ones who deserve it hear it least. Give it. Tell your manager one thing about how they work that you would take with you.
153	203	1	Name it so it can be protected. Whatever makes a team hard to replace is usually invisible to the people running it. Make it visible. Describe to your manager what the team has that would be hard to rebuild.
154	205	1	Test one connection before concluding. Detachment builds quietly, and it can be a settled judgement or just a run of bad weeks. Check which. Have one proper conversation with a colleague this week and see whether it shifts anything.
155	206	1	Give them one thing to respond to. A manager who seems indifferent is often simply uninformed about what you are doing and how it is going. Test it. Tell them something you are proud of and see what comes back.
156	207	1	Name what is actually missing. Having nothing to miss is worth understanding precisely, because the cause determines whether it is fixable here. Be specific. Write down the one thing that would have to change for this to be a team you would miss.
157	209	1	Say why, so it survives the next bad month. A choice you would repeat is worth being able to articulate, because the reasons get harder to find under pressure. Write it down. Note the two reasons you would choose this team again, and keep them.
158	210	1	Tell them, and tell their manager. Good managers are often invisible upward, and their teams are the only people who can correct that. Correct it. Say what your manager does well, once, to someone above them.
159	211	1	Protect the combination deliberately. Having both the work and the people right is rarer than it feels from inside it, and it usually degrades from one side. Notice which. Identify which of the two is more at risk and what would protect it.
160	213	1	Write down what would need to change. Doubt about staying is more useful as a list than as a feeling, because a list can be acted on or ruled out. Make one. Write the two things that would have to change for the answer to be yes.
161	214	1	Ask for one specific change rather than a different relationship. Difficulty with a manager rarely resolves in general terms, and one concrete request sometimes does. Make one. Identify a single change in how you work together, and ask for it directly.
162	215	1	Choose one part of it deliberately now. Ending up somewhere rather than choosing it is common, and the fix is claiming one piece of it rather than re-deciding everything. Claim one. Pick one part of the work you would choose, and ask for more of it.
163	217	1	Write the goal down in your own words. A goal you have heard stated clearly is not the same as one you could restate under pressure, and the gap only shows up when someone asks. Close it now. Write the team goal in one sentence in your own words, then check it against how your manager last described it.
164	219	1	Ask what the goal is this quarter and write the answer down. When goals keep changing people stop tracking them, which makes the next change harder to absorb. Get one fixed point. Ask your manager what the goal is for this quarter, write it down with the date, and check it again in a month.
\.


--
-- Data for Name: __EFMigrationsHistory; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."__EFMigrationsHistory" ("MigrationId", "ProductVersion") FROM stdin;
20260207225815_AddDocumentEmbeddingsWithPgVector	9.0.4
20260208122000_AddFileNameAndChunkIndexToDocumentEmbedding	9.0.4
20260427100000_AddInsightPublishLog	9.0.4
20260427110000_AddAuditFieldsToInsightPublishLog	9.0.4
20260427120000_AddOperationTypeToInsightPublishLog	9.0.4
20260427130000_RemoveInsightPublishLogCascade	9.0.4
20260512103740_AddSystemDefaultSettings	9.0.4
20260512122112_AddScheduleDateFromTo	9.0.4
20260512130818_RemoveScheduleDateFromTo	9.0.4
20260512144744_AddLastReportRunAt	9.0.4
20260513145426_RenameToLLMReportSchedules	9.0.4
20260601133926_AddStripeProductIdToCompanySubscription	9.0.4
20260603094118_AddNotesToCompanyLicense	9.0.4
20260717093650_AddEranCoachManagerMemory	9.0.4
20260811123252_AddTeamSizeToBusinessUnit	9.0.4
20260824140040_AddTeamQuestions	9.0.5
20260824155138_RedesignTeamQuestionsToMatchLegacy	9.0.5
20260824163703_AddTeamDailyFeedbackAuditLogs	9.0.5
20260825151648_AddTeamQuestionCategories	9.0.1
20260826090839_ReshapeCustomQuestions	9.0.5
20260826122923_AddMobileAppTables	9.0.5
20260827114040_AddUnitIdToCustomQuestionDailyFeedback	9.0.5
20260831095121_AddTeamSetupAndGoals	9.0.5
20260907091110_RenameCompaniesToProfiles	9.0.5
20260907102423_RenameCompanyIdToProfileId	9.0.5
20260907155849_RemoveIsCloveraPersonalApp	9.0.5
20260908090000_CollapseUserGroupsToTwoRoles	9.0.1
20260908120000_RenameUserTypeCompanyToProfileAdmin	9.0.1
20260908160000_AddRridaAdminAndPortfolioManagerRoles	9.0.1
20260908180000_AddIsNewTeamToUserTeams	9.0.1
20260910090000_AddUserRolesAndPortfolioManagerAssignments	9.0.1
20260916090000_AddIsPendingActivationToUserTeams	9.0.1
20260916143307_DailyCheckInMultiReasonsAndTeamFeedback	9.0.5
20260917090000_AllowThreeTeamsPerLicense	9.0.1
\.


--
-- Data for Name: workcategories; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.workcategories (id, category, title, subtitle) FROM stdin;
1	Meetings - Status/Update meetings	Update meetings	Gathering round the campfire
2	Meetings - Working sessions	Working sessions	Getting stuff done together
3	Meetings - One-to-ones	One-to-ones	No agenda, all opinions
4	Talking to clients	Talking to clients	The people who pay the bills
5	Focused work time	Focused work	Head down — do not disturb
6	Collaborative work	Collaborative work	More brains better than one
7	Reactive work	Reactive work	Unplanned "firefighting"
8	Emails	Emails & messages	Responding to my fans
9	Admin	Admin	Keeps the lights on
10	Getting help	Getting help	(and shouting at my screen)
11	Learning	Learning	A slightly better version of me
12	Travel	Travel	In transit, living out of a bag
\.


--
-- Name: AIModelConfigs_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."AIModelConfigs_Id_seq"', 1, false);


--
-- Name: AssignUserRights_AssignRightId_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."AssignUserRights_AssignRightId_seq"', 1, false);


--
-- Name: ContentCultures_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ContentCultures_Id_seq"', 1, false);


--
-- Name: DailyMicroActions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DailyMicroActions_Id_seq"', 1, false);


--
-- Name: DailyQuestionSetGroups_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DailyQuestionSetGroups_Id_seq"', 1, false);


--
-- Name: DailyQuestionSets_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DailyQuestionSets_Id_seq"', 1, false);


--
-- Name: DailyQuestionSpecifications_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DailyQuestionSpecifications_Id_seq"', 1, false);


--
-- Name: DailyQuestions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DailyQuestions_Id_seq"', 1, false);


--
-- Name: DailyReasons_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DailyReasons_Id_seq"', 1, false);


--
-- Name: Plans_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Plans_Id_seq"', 5, true);


--
-- Name: PromptTemplates_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PromptTemplates_Id_seq"', 1, true);


--
-- Name: SeedingHistories_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."SeedingHistories_Id_seq"', 1, true);


--
-- Name: TeamDailyMicroActions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TeamDailyMicroActions_Id_seq"', 164, true);


--
-- Name: TeamDailyQuestionSpecifications_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TeamDailyQuestionSpecifications_Id_seq"', 28, true);


--
-- Name: TeamDailyQuestions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TeamDailyQuestions_Id_seq"', 28, true);


--
-- Name: TeamDailyReasons_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TeamDailyReasons_Id_seq"', 220, true);


--
-- Name: TeamQuestionCategories_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TeamQuestionCategories_Id_seq"', 1, true);


--
-- Name: workcategories_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.workcategories_id_seq', 12, true);


--
-- PostgreSQL database dump complete
--


--
-- ModelConfiguration (ApiKey removed)
--
INSERT INTO public."ModelConfiguration" OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_record(NULL::public."ModelConfiguration", '{"Id": 1, "TopP": 0, "IsDel": false, "Source": "0", "Endpoint": "https://rag-multimodel-ai-foundry-prod.cognitiveservices.azure.com/", "Provider": "AI Foundary", "CreatedAt": "2026-03-04T13:53:20.112666", "CreatedBy": "60", "IsDefault": true, "MaxTokens": 4096, "ModelName": "gpt-4o", "ApiVersion": "2025-01-01-preview", "IsSelected": true, "ModifiedAt": "2026-04-10T10:45:14.860646", "ModifiedBy": "60", "Temperature": 0.7, "FriendlyName": "Search-model", "InputCostPer1k": 0.0025, "OutputCostPer1k": 0.01, "ModelConfigTypeId": 1}');
INSERT INTO public."ModelConfiguration" OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_record(NULL::public."ModelConfiguration", '{"Id": 2, "TopP": 0, "IsDel": false, "Source": "0", "Endpoint": "https://rag-multimodel-ai-foundry-prod.cognitiveservices.azure.com/", "Provider": "AI Foundary", "CreatedAt": "2026-03-04T13:57:22.090243", "CreatedBy": "60", "IsDefault": true, "MaxTokens": 4096, "ModelName": "embedding-model", "ApiVersion": "2023-05-15", "IsSelected": true, "ModifiedAt": "2026-04-06T10:13:23.228933", "ModifiedBy": "60", "Temperature": 0.7, "FriendlyName": "Embedding-model", "InputCostPer1k": 0, "OutputCostPer1k": 0, "ModelConfigTypeId": 3}');
INSERT INTO public."ModelConfiguration" OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_record(NULL::public."ModelConfiguration", '{"Id": 4, "TopP": 0, "IsDel": false, "Source": "0", "Endpoint": "https://rag-multimodel-ai-foundry-prod.cognitiveservices.azure.com/", "Provider": "AI Foundary", "CreatedAt": "2026-03-04T13:59:49.195318", "CreatedBy": "60", "IsDefault": true, "MaxTokens": 4096, "ModelName": "gpt-4.1-batch", "ApiVersion": "2025-01-01-preview", "IsSelected": true, "ModifiedAt": "2026-03-04T13:59:49.195318", "ModifiedBy": "60", "Temperature": 0.7, "FriendlyName": "Batch-model", "InputCostPer1k": 0, "OutputCostPer1k": 0, "ModelConfigTypeId": 2}');
INSERT INTO public."ModelConfiguration" OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_record(NULL::public."ModelConfiguration", '{"Id": 5, "TopP": 0, "IsDel": false, "Source": "0", "Endpoint": "https://rag-multimodel-ai-foundry-prod.services.ai.azure.com/models/chat/completions?api-version=2024-05-01-preview", "Provider": "AI Foundary", "CreatedAt": "2026-03-04T16:41:53.093615", "CreatedBy": "60", "IsDefault": false, "MaxTokens": 4096, "ModelName": "DeepSeek-V3-0324", "ApiVersion": "2024-05-01-preview", "IsSelected": true, "ModifiedAt": "2026-03-04T18:26:21.126055", "ModifiedBy": "60", "Temperature": 0.7, "FriendlyName": "Search-model", "InputCostPer1k": 0, "OutputCostPer1k": 0, "ModelConfigTypeId": 1}');
INSERT INTO public."ModelConfiguration" OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_record(NULL::public."ModelConfiguration", '{"Id": 6, "TopP": 0, "IsDel": false, "Source": "0", "Endpoint": "https://rag-multimodel-ai-foundry-prod.services.ai.azure.com/models/chat/completions?api-version=2024-05-01-preview", "Provider": "AI Foundary", "CreatedAt": "2026-03-04T18:26:21.150604", "CreatedBy": "60", "IsDefault": false, "MaxTokens": 4096, "ModelName": "grok-3", "ApiVersion": "2024-05-01-preview", "IsSelected": true, "ModifiedAt": "2026-03-04T18:26:21.150658", "ModifiedBy": "60", "Temperature": 0.7, "FriendlyName": "Search-model", "InputCostPer1k": 0, "OutputCostPer1k": 0, "ModelConfigTypeId": 1}');
INSERT INTO public."ModelConfiguration" OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_record(NULL::public."ModelConfiguration", E'{"Id": 7, "TopP": 0, "IsDel": false, "Source": "0", "Endpoint": "https://rag-multimodel-ai-foundry-prod.services.ai.azure.com/anthropic/v1/messages", "Provider": "AI Foundary", "CreatedAt": "2026-04-10T12:37:20.112666", "CreatedBy": "60", "IsDefault": false, "MaxTokens": 4096, "ModelName": "claude-sonnet-4-6\\n", "ApiVersion": "v1", "IsSelected": true, "ModifiedAt": "2026-04-10T12:38:14.860646", "ModifiedBy": "60", "Temperature": 0.7, "FriendlyName": "Search-model", "InputCostPer1k": 0.015, "OutputCostPer1k": 0.075, "ModelConfigTypeId": 1}');
INSERT INTO public."ModelConfiguration" OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_record(NULL::public."ModelConfiguration", E'{"Id": 8, "TopP": 0, "IsDel": false, "Source": "Claude", "Endpoint": "https://rag-multimodel-ai-foundry-prod.services.ai.azure.com/anthropic/v1/messages", "Provider": "AI Foundary", "CreatedAt": "2026-04-10T12:49:20.112666", "CreatedBy": "60", "IsDefault": false, "MaxTokens": 4096, "ModelName": "claude-opus-4-6\\n", "ApiVersion": "v1", "IsSelected": true, "ModifiedAt": "2026-04-10T12:41:14.860646", "ModifiedBy": "60", "Temperature": 0.7, "FriendlyName": "Search-model", "InputCostPer1k": 0.015, "OutputCostPer1k": 0.075, "ModelConfigTypeId": 1}');
SELECT pg_catalog.setval(pg_catalog.pg_get_serial_sequence('public."ModelConfiguration"', 'Id'),
                         (SELECT COALESCE(MAX("Id"), 1) FROM public."ModelConfiguration"));
