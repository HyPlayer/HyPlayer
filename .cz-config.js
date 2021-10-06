'use strict';

module.exports = {

    types: [{
            value: '⌛ WIP',
            name: '⌛ WIP:       Work in progress'
        },
        {
            value: '✨ Feat',
            name: '✨ feat:      A new feature'
        },
        {
            value: '➕ Add',
            name: '➕ add:       A new settings, layout, etc.'
        },
        {
            value: '⛓️ Dep',
            name: '⛓️ dep:       Fix dependency problems'
        },
        {
            value: '🐞 Fixed',
            name: '🐞 fixed:     A bug fix'
        },
        {
            value: '🛠 Refactor',
            name: '🛠 refactor:   A code change that neither fixes a bug nor adds a feature'
        },
        {
            value: '📚 Docs',
            name: '📚 docs:      Documentation only changes'
        },
        {
            value: '🧪 Test',
            name: '🧪 test:      Add a testing function'
        },
        {
            value: '🗯 Chore',
            name: '🗯 chore:      Changes that don\'t modify src or test files. Such as updating build tasks, package manager'
        },
        {
            value: '💅 Reformat',
            name: '💅 reformat:  Do the code reformat'
        },
        {
            value: '📦 Dump',
            name: '📦 dump:      New release version'
        },
        {
            value: '⏪ Revert',
            name: '⏪ revert:    Revert to a commit'
        },
        {
            value: '🗺️ Roadmap',
            name: '🗺️ roadmap:   Decide what will you done'
        },
        {
            value: '🎉 Init',
            name: '🎉 init:      Initial Commit'
        },
        {
            value: '🗑️ Remove',
            name: '🗑️ remove     Remove some obsolote code'
        },
        {
            value: '🥚 Egg',
            name: '🥚 egg        Add an egg~'
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
            value: '⚡️ Improve',
            name: '⚡️ Improve     Improve performance'
        }
    ],

    scopes: [],

    allowCustomScopes: true,
    allowBreakingChanges: ["feat", "fix", "init", "dump"]
};