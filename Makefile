.PHONY: streamhandler

# Builds and uploads the StreamHandler Lambda function
streamhandler:
	dotnet lambda package --project-location StreamHandler/src/StreamHandler/
	aws lambda update-function-code --profile dev-enc --function-name MessageProcessor-StreamHandler --zip-file fileb://StreamHandler/src/StreamHandler/bin/Release/netcoreapp3.1/StreamHandler.zip
