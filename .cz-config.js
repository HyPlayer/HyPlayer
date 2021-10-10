'use strict';

module.exports = {

    types: [{
            value: '⌛ WIP',
            name: '⌛ WIP:       Work in progress'
        },
        {
            value: '✨ Feat',
            name: '✨ Feat:      A new feature'
        },
        {
            value: '➕ Add',
            name: '➕ Add:       A new settings, layout, etc.'
        },
        {
            value: '⛓️ Dep',
            name: '⛓️ Dep:       Fix dependency problems'
        },
        {
            value: '🐞 Fixed',
            name: '🐞 Fixed:     A bug fix'
        },
        {
            value: '🛠️ Refactor',
            name: '🛠️ Refactor:  A code change that neither fixes a bug nor adds a feature'
        },
        {
            value: '📚 Docs',
            name: '📚 Docs:      Documentation only changes'
        },
        {
            value: '🧪 Test',
            name: '🧪 Test:      Add a testing function'
        },
        {
            value: '🗯️ Chore',
            name: '🗯️ Chore:     Changes that don\'t modify src or test files. Such as updating build tasks, package manager'
        },
        {
            value: '💅 Reformat',
            name: '💅 Reformat:  Do the code reformat'
        },
        {
            value: '📦 Dump',
            name: '📦 Dump:      New release version'
        },
        {
            value: '⏪ Revert',
            name: '⏪ Revert:    Revert to a commit'
        },
        {
            value: '🗺️ Roadmap',
            name: '🗺️ Roadmap:   Decide what will you done'
        },
        {
            value: '🎉 Init',
            name: '🎉 init:      Initial Commit'
        },
        {
            value: '🗑️ Remove',
            name: '🗑️ Remove     Remove some obsolote code'
        },
        {
            value: '🥚 Egg',
            name: '🥚 Egg        Add an egg~'
        },
        {
            value: '📸 Snapshot',
            name: '📸 Snapshot   Add or update the snapshot or preview image'
        },
        {
            value: '🔀 Merge',
            name: '🔀 Merge      Merge a branch or pull request'
        },
        {
            value: '✏️ Typo',
            name: '✏️ Typo       Fix a typo'
        },
        {
            value: '⚡ Improve',
            name: '⚡ Improve    Improve performance'
        },
		{
            value: '🖼️ UI',
            name: '🖼️ UI         Improve performance'
        },
		{
			value: '📝 Note',
			name: '📝 Note       Update README or other explanatory document'
		}
    ],

    scopes: [],

    allowCustomScopes: true,
    allowBreakingChanges: ["feat", "fix", "init", "dump"]
};