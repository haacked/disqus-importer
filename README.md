# Disqus Importer

A Disqus importer for [jekyll-blog-comments](https://github.com/damieng/jekyll-blog-comments) 
by @damieng.

Reads an [export of your Disqus comments](https://help.disqus.com/developer/comments-export) 
and generates yaml files and include files for a Jekyll site suitable for 
rendering comments on a website.

## Usage

`disqus-importer.exe PATH-TO-EXPORT PATH-TO-JEKYLL-DIRECTORY`

For example, if you export your disqus comments to a temp folder, you might
run the following command.

`disqus-importer.exe c:\temp\haacked-2018-05-11T17_11_33.342137-all.xml C:\repos\haacked.com`

Make sure there are no spaces in either paths. I haven't gotten around to add
real command line parsing. This is pretty raw code right now.

You've been warned!

Once you've imported all your comments, you'll want to tweak the comment
templates in `_includes` to fit your site's design. You'll also need to update
your layout.

Check out the instructions in https://github.com/damieng/jekyll-blog-comments
for more details.